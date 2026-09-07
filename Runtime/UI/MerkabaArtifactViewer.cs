using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.UI
{
    public enum MerkabaArtifactPaintTool
    {
        Brush,
        SurfaceBrush,
        SpatialBrush,
        Spray,
        Line,
        Erase,
        Eyedropper
    }

    /// <summary>
    /// Streamed, read-only preview of this package's own tiled GLB export.  It
    /// never reads canonical M8 and never becomes scan or export authority.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MerkabaExporter), typeof(RoomScanner))]
    public sealed class MerkabaArtifactViewer : MonoBehaviour
    {
        private const long LargePackageBytes = 1024L * 1024L * 1024L;
        private const int MaximumConcurrentTileLoads = 4;
        private const int GlbReadBufferBytes = 1024 * 1024;
        private const float InputDeadZone = 0.12f;
        private const float TranslationSpeed = 0.45f;
        private const float RotationSpeed = 75f;
        private const float ZoomSpeed = 1.4f;
        private const float MinimumAnnotationDrag = 0.012f;
        private const float AnnotationPointRadius = 0.025f;
        private const float AnnotationLineWidth = 0.01f;
        private const float AnnotationPlaneAlpha = 0.2f;
        private const float AnnotationHandleRadius = 0.018f;
        private const float AnnotationPickRadius = 0.045f;
        private const float PaintSurfaceOffset = 0.001f;
        private const float PaintEraseInterval = 0.06f;
        private const float ModelRayDistance = 1000f;
        private const uint GlbMagic = 0x46546c67u;
        private const uint JsonChunkType = 0x4e4f534au;
        private const uint BinaryChunkType = 0x004e4942u;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CapturedColorFactorId = Shader.PropertyToID("_CapturedColorFactor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int SourceBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DestinationBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaDitherId =
            Shader.PropertyToID("_AlphaDither");
        private static readonly int PlanColorId =
            Shader.PropertyToID("_PlanColor");
        private const string PlanKeyword = "M8_ARTIFACT_PLAN";

        [SerializeField] internal Shader previewShader;
        [SerializeField] private float initialPreviewSize = 0.65f;
        [SerializeField] private float initialPreviewDistance = 0.9f;
        [SerializeField] private float minimumPreviewScale = 0.025f;
        [SerializeField] private Color backdropColor =
            new(0.025f, 0.04f, 0.065f, 0.42f);
        [SerializeField] private Color architecturalPlanColor =
            new(0.32f, 0.95f, 0.68f, 1f);

        private MerkabaExporter _exporter;
        private RoomScanner _scanner;
        private MerkabaPersistence _persistence;
        private ControllerRayDriver _rayDriver;
        private MerkabaPaintEngine _paintEngine;
        private MerkabaDesignLibrary _designLibrary;
        private Transform _modelRoot;
        private Transform _designDisplayRoot;
        private Transform _annotationRoot;
        private GameObject _backdrop;
        private GameObject _continuationMarker;
        private Material _modelMaterial;
        private Material _annotationMaterial;
        private Material _annotationPlaneMaterial;
        private Material _backdropMaterial;
        private Material _continuationMaterial;
        private Mesh _pointMarkerMesh;
        private Mesh _continuationMesh;
        private GameObject _annotationHitPreview;
        private readonly List<Mesh> _annotationMeshes = new();
        private readonly Dictionary<int, GameObject> _annotationObjects = new();
        private readonly List<Tile> _tiles = new();
        private readonly List<AnnotationRecord> _annotations = new();
        private readonly List<(Tile Tile, float Score)> _desiredTiles = new();
        private readonly HashSet<Tile> _keptTiles = new();
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private bool _indexLoadPending;
        private bool _displaySuppressed;
        private bool _savedReadoutEnabled;
        private bool _savedFineMode;
        private bool _modelGrabActive;
        private bool _annotationPoseGrabActive;
        private bool _worldLocked = true;
        private bool _roomAligned;
        private bool _alignmentPending;
        private bool _packagePickerPending;
        private bool _designAssetPickerPending;
        private bool _paintInputEnabled;
        private bool _objectInputEnabled;
        private bool _objectSurfaceSnap = true;
        private bool _objectUprightSnap = true;
        private bool _objectGridSnap;
        private bool _planViewEnabled;
        private bool _hasPaintSample;
        private bool _hasSavedPreviewTransform;
        private bool _savedQueriesHitBackfaces;
        private bool _ownsQueriesHitBackfaces;
        private TouchScreenKeyboard _noteKeyboard;
        private string _noteBeforeKeyboard = string.Empty;
        private int _noteAnnotationId;
        private int _noteKeyboardOpenedFrame;
        private bool _noteKeyboardWasVisible;
        private int _generation;
        private int _alignmentRevision;
        private int _tileLoadsInFlight;
        private int _nextAnnotationId = 1;
        private float _nextResidencyRefresh;
        private float _previewOpacity = 1f;
        private float _paintWidth = 0.01f;
        private float _paintFlow = 0.65f;
        private float _paintHardness = 0.8f;
        private float _paintSaturation = 1f;
        private float _sprayDensity = 90f;
        private float _sprayScatter = 0.04f;
        private float _nextPaintErase;
        private MerkabaBrushShape _paintShape = MerkabaBrushShape.Round;
        private Color _paintColor = new(0.1f, 0.8f, 1f, 0.85f);
        private Vector3 _scanCenter;
        private ModelGrabMode _modelGrabMode;
        private MerkabaDesignLibrary.OneHandGrab _oneHandGrab;
        private MerkabaDesignLibrary.TwoHandGrab _twoHandGrab;
        private Vector3 _savedPreviewWorldPosition;
        private Quaternion _savedPreviewWorldRotation;
        private Vector3 _savedPreviewScale;
        private AnnotationMode _annotationMode;
        private AnnotationDrag _annotationDrag;
        private GameObject _annotationDraftObject;
        private LineRenderer _annotationDraftLine;
        private Mesh _annotationDraftMesh;
        private int _selectedAnnotationId;
        private Vector3 _moveStart;
        private Vector3[] _moveOriginalPoints;
        private int _moveHandleIndex = -1;
        private Plane _movePlane;
        private AnnotationPoseGrab _annotationPoseGrab;
        private bool _hasModelHit;
        private ModelHit _latestModelHit;
        private Ray _lastPaintRay;
        private Vector3 _lastPaintPoint;
        private Vector3 _lineStart;
        private Vector3 _lineNormal;
        private readonly List<MerkabaPaintEngine.PaintInputSample>
            _paintInputSamples = new();
        private MerkabaArtifactPaintTool _paintTool =
            MerkabaArtifactPaintTool.SurfaceBrush;
        private long _totalModelBytes;
        private long _totalResidentEstimateBytes;
        private string _archivePath;
        private string _annotationPath;
        private Guid _annotationSessionId;
        private bool _annotationsDirty;
        private bool _annotationsLoaded;
        private MerkabaSpatialBinding? _packageSpatialBinding;
        private Transform _artifactAnchor;
        private bool _ownsArtifactAnchor;
        private bool _deferredExportClose, _deferredExportDestroy;
        private bool ExportMutationHeld => _scanner?.ExportMutationHeld == true;

        public bool IsOpen { get; private set; }
        public bool HasSessionDesign => _paintEngine != null && _paintEngine.IsOpen;
        public string Status { get; private set; } = "GLB View closed";
        public string AnnotationModeText => _annotationMode.ToString().ToUpperInvariant();
        public bool HasSelectedAnnotation => FindSelectedAnnotation() != null;
        public bool PaintInputEnabled
        {
            get => _paintInputEnabled;
            set
            {
                if (ExportMutationHeld) return;
                if (_paintInputEnabled == value) return;
                _paintInputEnabled = value;
                CancelPaintStroke();
                if (value)
                {
                    CancelAnnotationDrag();
                    _annotationMode = AnnotationMode.Off;
                }
                RefreshTileColliders();
            }
        }
        public bool ObjectInputEnabled
        {
            get => _objectInputEnabled;
            set
            {
                if (ExportMutationHeld) return;
                if (_objectInputEnabled == value) return;
                _objectInputEnabled = value;
                _designLibrary?.EndGrab(true);
                if (!value) _designLibrary?.SetPlacementEnabled(false);
                RefreshTileColliders();
            }
        }
        public bool ObjectSurfaceSnap
        {
            get => _objectSurfaceSnap;
            set
            {
                _objectSurfaceSnap = value;
                RefreshTileColliders();
            }
        }
        public bool ObjectUprightSnap
        {
            get => _objectUprightSnap;
            set => _objectUprightSnap = value;
        }
        public bool ObjectGridSnap
        {
            get => _objectGridSnap;
            set => _objectGridSnap = value;
        }
        public IReadOnlyList<MerkabaDesignAsset> DesignAssets =>
            _designLibrary?.Assets ?? Array.Empty<MerkabaDesignAsset>();
        public IReadOnlyList<MerkabaDesignInstance> DesignInstances =>
            _designLibrary?.Instances ?? Array.Empty<MerkabaDesignInstance>();
        public string SelectedDesignAssetId =>
            _designLibrary?.SelectedAssetId ?? string.Empty;
        public int SelectedDesignInstanceId =>
            _designLibrary?.SelectedInstanceId ?? 0;
        public bool ObjectPlacementEnabled =>
            _designLibrary?.PlacementEnabled ?? false;
        public bool CanUndoDesign => _paintEngine?.CanUndo ?? false;
        public bool CanRedoDesign => _paintEngine?.CanRedo ?? false;
        public MerkabaArtifactPaintTool PaintTool
        {
            get => _paintTool;
            set
            {
                if (ExportMutationHeld) return;
                if (_paintTool == value) return;
                CancelPaintStroke();
                _paintTool = value;
                RefreshTileColliders();
                Status = "Paint " + value;
            }
        }
        public Color PaintColor
        {
            get => _paintColor;
            set => _paintColor = new Color(Mathf.Clamp01(value.r),
                Mathf.Clamp01(value.g), Mathf.Clamp01(value.b),
                Mathf.Clamp01(value.a));
        }
        public float PaintWidth
        {
            get => _paintWidth;
            set => _paintWidth = Mathf.Clamp(value, 0.002f, 0.1f);
        }
        public float PaintFlow
        {
            get => _paintFlow;
            set => _paintFlow = Mathf.Clamp01(value);
        }
        public float PaintHardness
        {
            get => _paintHardness;
            set => _paintHardness = Mathf.Clamp01(value);
        }
        public float PaintSaturation
        {
            get => _paintSaturation;
            set => _paintSaturation = Mathf.Clamp01(value);
        }
        public float SprayDensity
        {
            get => _sprayDensity;
            set => _sprayDensity = Mathf.Clamp(value, 1f, 300f);
        }
        public float SprayScatter
        {
            get => _sprayScatter;
            set => _sprayScatter = Mathf.Clamp(value, 0.005f, 0.25f);
        }
        public MerkabaBrushShape PaintShape
        {
            get => _paintShape;
            set => _paintShape = value;
        }
        public bool WorldLocked
        {
            get => _worldLocked;
            set
            {
                if (ExportMutationHeld) return;
                if (_worldLocked == value) return;
                if ((_roomAligned || _alignmentPending) && !value)
                {
                    ++_alignmentRevision;
                    ExitRoomAlignment();
                }
                _worldLocked = value;
                ApplyViewerFrame();
            }
        }
        public bool RoomAligned
        {
            get => _roomAligned;
            set => _ = SetRoomAlignedAsync(value);
        }
        public bool PlanViewEnabled
        {
            get => _planViewEnabled;
            set
            {
                if (_planViewEnabled == value) return;
                _planViewEnabled = value;
                ApplyPreviewOpacity();
                Status = value
                    ? "Architectural plan view enabled"
                    : "Measured GLB model view enabled";
            }
        }
        public float PreviewOpacity
        {
            get => _previewOpacity;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, _previewOpacity)) return;
                _previewOpacity = clamped;
                ApplyPreviewOpacity();
            }
        }
        public string SelectedNote
        {
            get
            {
                AnnotationRecord selected = FindSelectedAnnotation();
                return selected?.note ?? string.Empty;
            }
            set
            {
                AnnotationRecord selected = FindSelectedAnnotation();
                if (selected != null) SetAnnotationNote(selected.id, value);
            }
        }

        internal int LoadedTileCount
        {
            get
            {
                int count = 0;
                foreach (Tile tile in _tiles)
                    if (tile.Object != null) count++;
                return count;
            }
        }

        private void BindAnnotations()
        {
            _annotationSessionId = Guid.Empty;
            // A foreign preview never adopts or dirties the active session's
            // notes. A matching anchored package uses that session's document.
            if (_persistence != null && _persistence.HasActiveSession &&
                _packageSpatialBinding.HasValue &&
                _packageSpatialBinding.Value.IsValid &&
                _packageSpatialBinding.Value.AnchorUuid ==
                    _persistence.ActiveAnchorUuid)
            {
                _annotationSessionId = _persistence.ActiveSessionId;
                _annotationPath = _persistence.ActiveAnnotationsPath;
                return;
            }
            string archive = _archivePath;
            _annotationPath = string.IsNullOrWhiteSpace(archive) ? null :
                Path.Combine(Path.GetDirectoryName(archive),
                    Path.GetFileNameWithoutExtension(archive) +
                    ".annotations.json");
        }

        private void Awake()
        {
            _exporter = GetComponent<MerkabaExporter>();
            _scanner = GetComponent<RoomScanner>();
            _persistence = GetComponent<MerkabaPersistence>();
            _rayDriver = FindAnyObjectByType<ControllerRayDriver>();
            _paintEngine = GetComponent<MerkabaPaintEngine>() ??
                gameObject.AddComponent<MerkabaPaintEngine>();
            _paintEngine.Changed += OnPaintChanged;
        }

        private void Update()
        {
            if (ExportMutationHeld) return;
            PollNoteKeyboard();
            if (!IsOpen || _modelRoot == null) return;
            if (_scanner.IsScanning || _scanner.IsScanStarting)
            {
                Close();
                return;
            }
            if (_scanner.IsBusy)
            {
                CancelTransientInput();
                return;
            }
            HandleViewerInput();
            if (Time.unscaledTime >= _nextResidencyRefresh)
            {
                _nextResidencyRefresh = Time.unscaledTime + 0.2f;
                RefreshResidency();
            }
        }

        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close(true);
            if (_paintEngine != null)
                _paintEngine.Changed -= OnPaintChanged;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && !ExportMutationHeld) CancelTransientInput();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && !ExportMutationHeld) CancelTransientInput();
        }

        public async Task ToggleAsync()
        {
            if (IsOpen)
            {
                Close();
                return;
            }
            await OpenAsync();
        }

        public Task<bool> OpenAsync() => OpenArchiveAsync(
            _exporter.ViewerPackagePath);

        public async Task<bool> OpenArchiveAsync(string archivePath)
        {
            if (ExportMutationHeld || _scanner.IsBusy || _indexLoadPending) return false;
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                Status = "3D Tiles ZIP path is empty";
                return false;
            }
            archivePath = Path.GetFullPath(archivePath);
            if (!File.Exists(archivePath))
            {
                Status = "Model package does not exist";
                return false;
            }
            // Native models restore their own isolated committed session and
            // ordinary readout. A declared invalid package must not fall back
            // to a preview, and OPEN owns closing the current design/viewer.
            bool? native = await _scanner.ImportNativePackageAsync(archivePath);
            if (native.HasValue)
            {
                Status = native.Value ? "Native model opened in scan readout" :
                    _scanner.CurrentOperation.StatusText;
                return native.Value;
            }
            if (!isActiveAndEnabled || ExportMutationHeld || _scanner.IsBusy) return false;
            if (IsOpen)
            {
                if (string.Equals(_archivePath, archivePath,
                        StringComparison.Ordinal))
                    return true;
                Close();
                if (IsOpen) return false;
            }
            if (_indexLoadPending) return false;
            if (previewShader == null)
            {
                Status = "GLB View shader is not wired";
                return false;
            }
            if (!File.Exists(archivePath))
            {
                Status = "3D Tiles ZIP does not exist";
                return false;
            }

            int generation = ++_generation;
            _indexLoadPending = true;
            Status = "Opening preview-only 3D Tiles…";
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_scanner.IsScanning || _scanner.IsScanStarting)
                {
                    bool stopped = await _scanner.QuiesceScanningAsync();
                    if (!stopped)
                        throw new InvalidOperationException(
                            "Scanner GPU work did not retire for GLB View.");
                }
                if (generation != _generation || !isActiveAndEnabled || ExportMutationHeld)
                    return false;

                PackageIndex package = await Task.Run(() =>
                    ReadPackageIndex(archivePath));
                if (generation != _generation || !isActiveAndEnabled || ExportMutationHeld)
                    return false;

                _savedReadoutEnabled = _scanner.ReadoutDrawEnabled;
                _savedFineMode = _scanner.FineMode;
                _scanner.ReadoutDrawEnabled = false;
                _scanner.FineMode = false;
                _displaySuppressed = true;
                _savedQueriesHitBackfaces = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                _ownsQueriesHitBackfaces = true;
                _archivePath = archivePath;
                _packageSpatialBinding = package.SpatialBinding;
                CreatePreview(package);
                OpenSessionDesign();
                BindAnnotations();
                LoadAnnotations();
                IsOpen = true;
                Status = $"GLB preview-only · 0/{_tiles.Count} tiles";
                RefreshResidency();
                Logger.Info($"Merkaba GLB View index ready in " +
                    $"{timer.Elapsed.TotalMilliseconds:F1} ms: " +
                    $"tiles={_tiles.Count}, model={_totalModelBytes} bytes, " +
                    $"residentEstimate={_totalResidentEstimateBytes} bytes.");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba artifact preview failed: " + exception);
                Status = "GLB View failed: " + exception.Message;
                Close();
                return false;
            }
            finally
            {
                if (generation == _generation) _indexLoadPending = false;
            }
        }

        public void RequestPackageFromDisk()
        {
            if (ExportMutationHeld || _scanner.IsBusy) return;
            if (_packagePickerPending || _indexLoadPending)
                return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (IsOpen) Close();
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<
                    AndroidJavaObject>("currentActivity");
                using var picker = new AndroidJavaClass(
                    "com.genesis.roomscan.MerkabaPackagePicker");
                _packagePickerPending = true;
                Status = "Choose a 3D Tiles ZIP…";
                picker.CallStatic("open", activity, gameObject.name,
                    nameof(OnPackagePickerResult));
            }
            catch (Exception exception)
            {
                _packagePickerPending = false;
                Logger.Error("Could not open 3D Tiles picker: " + exception);
                Status = "3D Tiles picker failed: " + exception.Message;
            }
#else
            Status = "3D Tiles disk picker is available on Quest";
#endif
        }

        /// <summary>Android document-picker callback. Called by UnitySendMessage.</summary>
        public void OnPackagePickerResult(string result)
        {
            _packagePickerPending = false;
            if (string.IsNullOrWhiteSpace(result) || result == "CANCELLED")
            {
                Status = "3D Tiles load cancelled";
                return;
            }
            const string errorPrefix = "ERROR:";
            if (result.StartsWith(errorPrefix, StringComparison.Ordinal))
            {
                Status = "3D Tiles import failed: " +
                    result.Substring(errorPrefix.Length);
                Logger.Error(Status);
                return;
            }
            _ = OpenArchiveAsync(result);
        }

        public void RequestDesignAssetFromDisk()
        {
            if (ExportMutationHeld || _scanner.IsBusy) return;
            if (_designAssetPickerPending) return;
            // A native GLB opens its own session and does not need a preview
            // design library. Preview-only assets still require one after the
            // package discriminator has returned null.
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<
                    AndroidJavaObject>("currentActivity");
                using var picker = new AndroidJavaClass(
                    "com.genesis.roomscan.MerkabaPackagePicker");
                _designAssetPickerPending = true;
                Status = "Choose a native GLB model or preview object…";
                picker.CallStatic("openGlb", activity, gameObject.name,
                    nameof(OnDesignAssetPickerResult));
            }
            catch (Exception exception)
            {
                _designAssetPickerPending = false;
                Logger.Error("Could not open design GLB picker: " + exception);
                Status = "Design GLB picker failed: " + exception.Message;
            }
#else
            Status = "Design GLB disk picker is available on Quest";
#endif
        }

        /// <summary>Android document-picker callback. Called by UnitySendMessage.</summary>
        public void OnDesignAssetPickerResult(string result)
        {
            _designAssetPickerPending = false;
            if (ExportMutationHeld) return;
            if (string.IsNullOrWhiteSpace(result) || result == "CANCELLED")
            {
                Status = "Design object import cancelled";
                return;
            }
            const string errorPrefix = "ERROR:";
            if (result.StartsWith(errorPrefix, StringComparison.Ordinal))
            {
                Status = "Design object import failed: " +
                    result.Substring(errorPrefix.Length);
                Logger.Error(Status);
                return;
            }
            _ = ImportDesignAssetAsync(result);
        }

        public bool SelectDesignAsset(string assetId) =>
            !ExportMutationHeld && (_designLibrary?.SelectAsset(assetId) ?? false);

        public void SetObjectPlacementEnabled(bool enabled)
        {
            if (ExportMutationHeld) return;
            _designLibrary?.SetPlacementEnabled(enabled);
            RefreshTileColliders();
        }

        public bool SelectDesignInstance(int instanceId) =>
            !ExportMutationHeld && (_designLibrary?.SelectInstance(instanceId) ?? false);

        public bool DuplicateSelectedDesignObject() =>
            !ExportMutationHeld && (_designLibrary?.DuplicateSelected() ?? false);

        public bool DeleteSelectedDesignObject() =>
            !ExportMutationHeld && (_designLibrary?.DeleteSelected() ?? false);

        public bool ToggleSelectedDesignObjectVisible() =>
            !ExportMutationHeld && (_designLibrary?.ToggleSelectedVisible() ?? false);

        public bool ToggleSelectedDesignObjectLocked() =>
            !ExportMutationHeld && (_designLibrary?.ToggleSelectedLocked() ?? false);

        public bool UndoDesign()
        {
            if (ExportMutationHeld) return false;
            _designLibrary?.EndGrab(true);
            if (_paintEngine == null || !_paintEngine.Undo()) return false;
            _designLibrary?.RefreshInstances();
            RefreshTileColliders();
            Status = "Design change undone";
            return true;
        }

        public bool RedoDesign()
        {
            if (ExportMutationHeld) return false;
            _designLibrary?.EndGrab(true);
            if (_paintEngine == null || !_paintEngine.Redo()) return false;
            _designLibrary?.RefreshInstances();
            RefreshTileColliders();
            Status = "Design change restored";
            return true;
        }

        private async Task ImportDesignAssetAsync(string importedPath)
        {
            if (ExportMutationHeld || _scanner.IsBusy) return;
            try
            {
                bool? native = await _scanner.ImportNativePackageAsync(importedPath);
                if (native.HasValue)
                {
                    Status = native.Value ? "Native model opened in scan readout" :
                        _scanner.CurrentOperation.StatusText;
                    return;
                }
                if (!isActiveAndEnabled || ExportMutationHeld || _scanner.IsBusy) return;
                MerkabaDesignLibrary library = _designLibrary ??
                    throw new InvalidOperationException(
                        "Open an anchored session before importing objects.");
                MerkabaDesignAsset asset = await Task.Run(() =>
                    library.ImportFile(importedPath));
                if (_designLibrary != library || !IsOpen || ExportMutationHeld) return;
                library.Refresh();
                library.SelectAsset(asset.id);
                library.SetPlacementEnabled(true);
                ObjectInputEnabled = true;
                Status = "Place preview-only object: " + asset.displayName;
            }
            catch (Exception exception)
            {
                Logger.Error("Design GLB import failed: " + exception);
                Status = "Design object import failed: " + exception.Message;
            }
            finally
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    string imports = Path.Combine(
                        Application.persistentDataPath, "MerkabaScan",
                        "imports");
                    string full = Path.GetFullPath(importedPath);
                    if (string.Equals(Path.GetDirectoryName(full), imports,
                            StringComparison.Ordinal) && File.Exists(full))
                        File.Delete(full);
                }
                catch (Exception exception)
                {
                    Logger.Warning("Could not remove staged design import: " +
                        exception.Message);
                }
#endif
            }
        }

        public void Close() => Close(false);

        // RoomScanner calls this on the original managed viewer reference,
        // after every export worker has left and the mutation hold is clear.
        // No polling task or early teardown of the frozen design is needed.
        internal void CompleteDeferredExportClose()
        {
            if (ExportMutationHeld || !_deferredExportClose) return;
            bool destroying = _deferredExportDestroy;
            _deferredExportClose = _deferredExportDestroy = false;
            Close(destroying);
        }

        private void Close(bool destroying)
        {
            if (ExportMutationHeld)
            {
                _deferredExportClose = true;
                _deferredExportDestroy |= destroying;
                return;
            }
            bool saved = SaveDesign();
            if (!saved && !destroying) return;
            bool wasOpen = IsOpen;
            ++_generation;
            ++_alignmentRevision;
            _indexLoadPending = false;
            _alignmentPending = false;
            _designAssetPickerPending = false;
            _tileLoadsInFlight = 0;
            IsOpen = false;
            _annotationMode = AnnotationMode.Off;
            _roomAligned = false;
            _hasSavedPreviewTransform = false;
            _selectedAnnotationId = 0;
            _moveOriginalPoints = null;
            _moveHandleIndex = -1;
            CancelTransientInput();
            CloseNoteKeyboard();
            if (_paintEngine != null)
            {
                _designLibrary?.CloseRuntime();
                _designLibrary = null;
                _paintEngine.Close();
            }
            _annotations.Clear();
            _annotationPath = null;
            _annotationSessionId = Guid.Empty;
            _annotationsDirty = false;
            _annotationsLoaded = false;
            _annotationObjects.Clear();
            foreach (Tile tile in _tiles) DestroyTile(tile);
            _tiles.Clear();
            _totalModelBytes = 0L;
            _totalResidentEstimateBytes = 0L;
            DestroyAnnotationMeshes();
            if (_pointMarkerMesh != null) Destroy(_pointMarkerMesh);
            _pointMarkerMesh = null;
            if (_continuationMesh != null) Destroy(_continuationMesh);
            _continuationMesh = null;
            if (_backdrop != null) Destroy(_backdrop);
            _backdrop = null;
            if (_continuationMarker != null) Destroy(_continuationMarker);
            _continuationMarker = null;
            if (_annotationHitPreview != null) Destroy(_annotationHitPreview);
            _annotationHitPreview = null;
            if (_modelRoot != null) Destroy(_modelRoot.gameObject);
            _modelRoot = null;
            _designDisplayRoot = null;
            _annotationRoot = null;
            ReleaseArtifactAnchor();
            if (_modelMaterial != null) Destroy(_modelMaterial);
            if (_annotationMaterial != null) Destroy(_annotationMaterial);
            if (_annotationPlaneMaterial != null)
                Destroy(_annotationPlaneMaterial);
            if (_backdropMaterial != null) Destroy(_backdropMaterial);
            if (_continuationMaterial != null) Destroy(_continuationMaterial);
            _modelMaterial = null;
            _annotationMaterial = null;
            _annotationPlaneMaterial = null;
            _backdropMaterial = null;
            _continuationMaterial = null;
            if (_displaySuppressed && _scanner != null)
            {
                _scanner.ReadoutDrawEnabled = _savedReadoutEnabled;
                _scanner.FineMode = _savedFineMode;
            }
            _displaySuppressed = false;
            _archivePath = null;
            _packageSpatialBinding = null;
            if (_ownsQueriesHitBackfaces)
            {
                Physics.queriesHitBackfaces = _savedQueriesHitBackfaces;
                _ownsQueriesHitBackfaces = false;
            }
            if (wasOpen && saved) Status = "GLB View closed";
        }

        public bool SaveDesign()
        {
            if (ExportMutationHeld) return false;
            // Freeze completed note input and transient gestures before the
            // existing session SAVE/Save-As cut begins its asynchronous work.
            PollNoteKeyboard();
            CloseNoteKeyboard();
            CancelTransientInput();
            bool annotationsSaved = TrySaveAnnotations();
            bool paintSaved = _paintEngine == null || _paintEngine.Save();
            if (!paintSaved) Status = "Session design save failed";
            return annotationsSaved && paintSaved;
        }

        internal void RebindSessionDesign()
        {
            if (!IsOpen) return;
            if (!SaveDesign()) return;
            OpenSessionDesign();
            BindAnnotations();
            LoadAnnotations();
        }

        private void OpenSessionDesign()
        {
            _paintEngine ??= GetComponent<MerkabaPaintEngine>() ??
                gameObject.AddComponent<MerkabaPaintEngine>();
            _designLibrary?.CloseRuntime();
            _designLibrary = null;
            _paintEngine.Save();
            _paintEngine.Close();
            if (_designDisplayRoot != null)
                Destroy(_designDisplayRoot.gameObject);
            _designDisplayRoot = null;
            string path = _scanner?.ActiveDesignPath;
            if (!RoomSpaceRoot.RoomSpaceReady || _modelRoot == null ||
                string.IsNullOrWhiteSpace(path) ||
                !_packageSpatialBinding.HasValue ||
                !_packageSpatialBinding.Value.IsValid ||
                _packageSpatialBinding.Value.AnchorUuid !=
                    _scanner.ActiveAnchorUuid)
            {
                Logger.Warning("Session design requires the preview package's " +
                    "actual anchor to match the active anchored session.");
                return;
            }
            Matrix4x4 local = ComposeSessionDesignLocal(
                _packageSpatialBinding.Value.AnchorFromPackage, _scanCenter);
            if (!TryDecomposeTransform(local, out Vector3 position,
                    out Quaternion rotation, out Vector3 scale))
            {
                Logger.Warning("Session design display frame is invalid.");
                return;
            }
            _designDisplayRoot = new GameObject("Session Design Display").transform;
            _designDisplayRoot.SetParent(_modelRoot, false);
            _designDisplayRoot.localPosition = position;
            _designDisplayRoot.localRotation = rotation;
            _designDisplayRoot.localScale = scale;
            _paintEngine.Open(_designDisplayRoot, previewShader, path);
            string libraryPath = _scanner?.DesignLibraryPath;
            if (string.IsNullOrWhiteSpace(libraryPath)) return;
            _designLibrary = new MerkabaDesignLibrary(libraryPath);
            _designLibrary.Open(_paintEngine.Document, _designDisplayRoot,
                previewShader, _paintEngine.MarkDocumentChanged,
                _paintEngine.BeginDocumentChange,
                _paintEngine.CommitDocumentChange,
                _paintEngine.RollbackDocumentChange);
        }

        private void OnPaintChanged() => _scanner?.MarkDesignDirty();

        public void CycleAnnotationMode()
        {
            if (ExportMutationHeld) return;
            _paintInputEnabled = false;
            CancelPaintStroke();
            CancelAnnotationDrag();
            SetAnnotationHitPreview(false, default);
            _annotationMode = (AnnotationMode)(((int)_annotationMode + 1) %
                Enum.GetValues(typeof(AnnotationMode)).Length);
            _moveOriginalPoints = null;
            RefreshTileColliders();
            Status = "Annotation " + AnnotationModeText;
        }

        public void DeleteSelectedAnnotation()
        {
            if (ExportMutationHeld) return;
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected == null)
            {
                Status = "Select an annotation to delete";
                return;
            }
            CloseNoteKeyboard();
            _annotations.Remove(selected);
            _selectedAnnotationId = 0;
            MarkAnnotationsDirty();
            RefreshAnnotationObjects();
            Status = $"Deleted {selected.type} #{selected.id}";
        }

        public void BeginNoteEdit()
        {
            if (ExportMutationHeld) return;
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected == null)
            {
                Status = "Select a point, line or plane first";
                return;
            }
            if (_noteKeyboard != null &&
                _noteKeyboard.status == TouchScreenKeyboard.Status.Visible)
                return;
            CloseNoteKeyboard();
            _noteBeforeKeyboard = selected.note ?? string.Empty;
            _noteAnnotationId = selected.id;
            _noteKeyboardOpenedFrame = Time.frameCount;
            _noteKeyboardWasVisible = false;
            _noteKeyboard = TouchScreenKeyboard.Open(_noteBeforeKeyboard,
                TouchScreenKeyboardType.Default, false, false, false, false,
                "Annotation note", 512);
            if (_noteKeyboard != null) _noteKeyboard.characterLimit = 512;
            Logger.Info($"Merkaba GLB note keyboard requested: " +
                $"supported={TouchScreenKeyboard.isSupported}, " +
                $"annotation={_noteAnnotationId}.");
            Status = _noteKeyboard != null
                ? $"Editing {selected.type} #{selected.id}"
                : "Quest system keyboard is unavailable";
        }

        private void PollNoteKeyboard()
        {
            if (ExportMutationHeld) return;
            if (_noteKeyboard == null) return;
            TouchScreenKeyboard.Status keyboardStatus = _noteKeyboard.status;
            if (keyboardStatus == TouchScreenKeyboard.Status.Visible ||
                _noteKeyboard.active)
            {
                _noteKeyboardWasVisible = true;
                return;
            }
            // Android may not report Visible until the overlay owns focus.
            // Do not interpret the request-frame default state as completion.
            if (!_noteKeyboardWasVisible &&
                Time.frameCount <= _noteKeyboardOpenedFrame + 2)
                return;
            bool committed = keyboardStatus ==
                TouchScreenKeyboard.Status.Done;
            if (committed)
                SetAnnotationNote(_noteAnnotationId, _noteKeyboard.text);
            AnnotationRecord selected = _annotations.Find(item =>
                item.id == _noteAnnotationId);
            if (selected != null)
                Status = committed
                    ? $"Updated note for {selected.type} #{selected.id}"
                    : $"Note unchanged for {selected.type} #{selected.id}";
            Logger.Info($"Merkaba GLB note keyboard retired: " +
                $"status={keyboardStatus}, annotation={_noteAnnotationId}.");
            _noteKeyboard = null;
            _noteBeforeKeyboard = string.Empty;
            _noteAnnotationId = 0;
            _noteKeyboardWasVisible = false;
        }

        private void CloseNoteKeyboard()
        {
            if (_noteKeyboard != null)
                _noteKeyboard.active = false;
            _noteKeyboard = null;
            _noteBeforeKeyboard = string.Empty;
            _noteAnnotationId = 0;
            _noteKeyboardWasVisible = false;
        }

        private void SetAnnotationNote(int annotationId, string value)
        {
            if (ExportMutationHeld) return;
            AnnotationRecord annotation = _annotations.Find(item =>
                item.id == annotationId);
            if (annotation == null) return;
            string note = value ?? string.Empty;
            if (string.Equals(annotation.note, note,
                    StringComparison.Ordinal)) return;
            annotation.note = note;
            MarkAnnotationsDirty();
        }

        public void SaveAnnotations()
        {
            if (!IsOpen)
            {
                Status = "Open a model before saving annotations";
                return;
            }
            if (!SaveDesign()) return;
            Status = $"Saved {_annotations.Count} survey annotations " +
                $"and {_paintEngine?.StrokeCount ?? 0} paint strokes";
        }

        private void MarkAnnotationsDirty()
        {
            _annotationsDirty = true;
            if (_annotationSessionId != Guid.Empty && _persistence != null &&
                _annotationSessionId == _persistence.ActiveSessionId)
                _persistence.MarkDirty();
        }

        private bool TrySaveAnnotations()
        {
            if (!_annotationsDirty) return true;
            try
            {
                if (!_annotationsLoaded || string.IsNullOrWhiteSpace(_annotationPath))
                    throw new IOException("Annotation document is not safely loaded.");
                // This path belongs to the loaded document, not whichever
                // session became active while Save-As was awaiting storage.
                string path = _annotationPath;
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory))
                    throw new IOException("Annotation destination has no directory.");
                Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                var file = new AnnotationFile
                {
                    format = "QuestMerkabaAnnotations",
                    version = 2,
                    nextId = _nextAnnotationId,
                    items = _annotations.ToArray()
                };
                byte[] bytes = Encoding.UTF8.GetBytes(
                    JsonUtility.ToJson(file, true));
                using (var stream = new FileStream(temporary, FileMode.Create,
                           FileAccess.Write, FileShare.None, 64 * 1024,
                           FileOptions.SequentialScan))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                MerkabaFilePublishing.Publish(temporary, path);
                _annotationsDirty = false;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Could not save GLB View annotations: " + exception);
                Status = "Annotation save failed: " + exception.Message;
                return false;
            }
        }

        private void CreatePreview(PackageIndex package)
        {
            _scanCenter = package.Bounds.center;
            _tiles.AddRange(package.Tiles);
            _totalModelBytes = package.TotalModelBytes;
            _totalResidentEstimateBytes = package.TotalResidentEstimateBytes;

            var root = new GameObject("Merkaba Export Preview");
            _modelRoot = root.transform;
            var annotations = new GameObject("Annotations");
            _annotationRoot = annotations.transform;
            _annotationRoot.SetParent(_modelRoot, false);

            _modelMaterial = new Material(previewShader)
            {
                name = "Merkaba GLB Preview",
                hideFlags = HideFlags.DontSave
            };
            _annotationMaterial = new Material(previewShader)
            {
                name = "Merkaba GLB Annotations",
                hideFlags = HideFlags.DontSave
            };
            ConfigureMaterial(_annotationMaterial, Color.white, false);
            _annotationPlaneMaterial = new Material(previewShader)
            {
                name = "Merkaba GLB Annotation Planes",
                hideFlags = HideFlags.DontSave
            };
            ConfigureMaterial(_annotationPlaneMaterial, Color.white, false);
            _previewOpacity = _scanner != null ? _scanner.ScanOpacity : 1f;
            ApplyPreviewOpacity();

            Camera camera = Camera.main;
            Vector3 forward = camera != null
                ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up)
                : Vector3.forward;
            if (forward.sqrMagnitude < 1e-5f) forward = Vector3.forward;
            forward.Normalize();
            float maximum = Mathf.Max(0.01f, Mathf.Max(package.Bounds.size.x,
                Mathf.Max(package.Bounds.size.y, package.Bounds.size.z)));
            float scale = Mathf.Max(minimumPreviewScale,
                initialPreviewSize / maximum);
            _modelRoot.localScale = Vector3.one * scale;
            _modelRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
            _modelRoot.position = camera != null
                ? camera.transform.position + forward * initialPreviewDistance -
                  Vector3.up * 0.08f
                : new Vector3(0f, 1.4f, 0.9f);
            ApplyViewerFrame();
            CreateBackdrop(camera);
        }

        private void HandleViewerInput()
        {
            _rayDriver ??= FindAnyObjectByType<ControllerRayDriver>();
            if (_rayDriver != null && _rayDriver.IsPointingAtUi)
            {
                CancelTransientInput();
                return;
            }
            bool rightGrip = OVRInput.Get(
                OVRInput.Button.SecondaryHandTrigger);
            bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger);
            Vector3 rightPosition = default;
            Quaternion rightRotation = default;
            bool hasRightPose = _rayDriver != null &&
                _rayDriver.TryGetWorldPose(out rightPosition,
                    out rightRotation);
            Vector3 leftPosition = default;
            Quaternion leftRotation = default;
            bool hasLeftPose = _rayDriver != null &&
                _rayDriver.TryGetLeftWorldPose(out leftPosition,
                    out leftRotation);
            Vector3 rayOrigin = default;
            Vector3 rayDirection = default;
            bool hasRay = _rayDriver != null &&
                _rayDriver.TryGetWorldRay(out rayOrigin,
                    out rayDirection);
            var ray = new Ray(rayOrigin, rayDirection);

            if (_objectInputEnabled)
            {
                HandleObjectInput(ray, hasRay, rightGrip, leftGrip,
                    hasRightPose, rightPosition, rightRotation, hasLeftPose,
                    leftPosition, leftRotation);
                return;
            }

            if (!_roomAligned && rightGrip && hasRightPose && hasRay &&
                (_annotationPoseGrabActive || TryBeginAnnotationPoseGrab(ray,
                    rightPosition, rightRotation)))
            {
                ContinueAnnotationPoseGrab(rightPosition, rightRotation);
                return;
            }
            _annotationPoseGrabActive = false;

            if (!_roomAligned && rightGrip && leftGrip && hasRightPose &&
                hasLeftPose)
            {
                ContinueTwoHandModelGrab(leftPosition, leftRotation,
                    rightPosition, rightRotation);
                return;
            }
            if (!_roomAligned && rightGrip && hasRightPose)
            {
                ContinueOneHandModelGrab(rightPosition, rightRotation);
                return;
            }
            EndModelGrab();

            Camera camera = Camera.main;
            Vector2 leftStick = ApplyDeadZone(OVRInput.Get(
                OVRInput.Axis2D.PrimaryThumbstick));
            if (!_roomAligned && leftGrip && Mathf.Abs(leftStick.y) > 0f)
            {
                float scale = Mathf.Clamp(_modelRoot.localScale.x *
                    Mathf.Exp(leftStick.y * ZoomSpeed * Time.unscaledDeltaTime),
                    0.002f, 2f);
                _modelRoot.localScale = Vector3.one * scale;
            }
            else if (!_roomAligned && camera != null &&
                     leftStick.sqrMagnitude > 0f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    camera.transform.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                _modelRoot.position += (right * leftStick.x +
                    forward * leftStick.y) * TranslationSpeed *
                    Time.unscaledDeltaTime;
            }

            Vector2 rightStick = ApplyDeadZone(OVRInput.Get(
                OVRInput.Axis2D.SecondaryThumbstick));
            if (!_roomAligned && Mathf.Abs(rightStick.x) > 0f)
                _modelRoot.Rotate(Vector3.up,
                    -rightStick.x * RotationSpeed * Time.unscaledDeltaTime,
                    Space.World);
            if (!_roomAligned && camera != null &&
                Mathf.Abs(rightStick.y) > 0f)
                _modelRoot.Rotate(camera.transform.right,
                    rightStick.y * RotationSpeed * Time.unscaledDeltaTime,
                    Space.World);

            if (!hasRay)
            {
                SetAnnotationHitPreview(false, default);
                if (!OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger))
                    CancelPaintStroke();
                return;
            }
            if (_paintInputEnabled)
            {
                HandlePaintInput(ray);
                return;
            }
            UpdateAnnotationHitPreview(ray);
            bool triggerDown = OVRInput.GetDown(
                OVRInput.Button.SecondaryIndexTrigger);
            bool triggerHeld = OVRInput.Get(
                OVRInput.Button.SecondaryIndexTrigger);
            bool triggerUp = OVRInput.GetUp(
                OVRInput.Button.SecondaryIndexTrigger);
            if (_annotationMode == AnnotationMode.Move)
            {
                if (triggerDown) BeginMove(ray);
                if (triggerHeld) ContinueMove(ray);
                if (triggerUp)
                {
                    _moveOriginalPoints = null;
                    _moveHandleIndex = -1;
                }
            }
            else if (_annotationMode == AnnotationMode.Select)
            {
                if (triggerDown) SelectAnnotation(ray);
            }
            else if (_annotationMode == AnnotationMode.Point)
            {
                if (triggerDown) AddPointAnnotation(ray);
            }
            else if (_annotationMode is AnnotationMode.Line or
                     AnnotationMode.Plane)
            {
                if (triggerDown) BeginAnnotationDrag(ray);
                if (triggerHeld) ContinueAnnotationDrag(ray);
                if (triggerUp) CompleteAnnotationDrag(ray);
            }
        }

        private void HandleObjectInput(Ray ray, bool hasRay, bool rightGrip,
            bool leftGrip, bool hasRightPose, Vector3 rightPosition,
            Quaternion rightRotation, bool hasLeftPose, Vector3 leftPosition,
            Quaternion leftRotation)
        {
            if (_designLibrary == null)
            {
                Status = "Open an anchored session before placing objects";
                return;
            }
            if (_designLibrary.PlacementEnabled && hasRay)
            {
                ModelHit surface = default;
                bool hit = _objectSurfaceSnap && TryHitModel(ray,
                    out surface);
                _designLibrary.UpdatePlacementPreview(ray, hit,
                    hit ? surface.Point : default,
                    hit ? surface.Normal : default, _objectSurfaceSnap,
                    _objectUprightSnap, _objectGridSnap);
                if (OVRInput.GetDown(
                        OVRInput.Button.SecondaryIndexTrigger))
                {
                    if (_designLibrary.PlaceSelected())
                    {
                        Status = "Design object placed";
                        RefreshTileColliders();
                    }
                }
                return;
            }
            if (rightGrip && leftGrip && hasRightPose && hasLeftPose)
            {
                _designLibrary.ContinueTwoHandGrab(leftPosition, leftRotation,
                    rightPosition, rightRotation);
                return;
            }
            if (rightGrip && hasRightPose)
            {
                _designLibrary.ContinueOneHandGrab(rightPosition,
                    rightRotation);
                return;
            }
            _designLibrary.EndGrab(true);
            if (hasRay && OVRInput.GetDown(
                    OVRInput.Button.SecondaryIndexTrigger))
                Status = _designLibrary.SelectInstance(ray)
                    ? "Design object selected"
                    : "No design object selected";
        }

        private static Vector2 ApplyDeadZone(Vector2 value)
        {
            float magnitude = value.magnitude;
            if (magnitude <= InputDeadZone) return Vector2.zero;
            float scaled = Mathf.InverseLerp(InputDeadZone, 1f,
                Mathf.Min(1f, magnitude));
            return value / magnitude * scaled;
        }

        private void ContinueOneHandModelGrab(Vector3 controllerPosition,
            Quaternion controllerRotation)
        {
            if (!_modelGrabActive || _modelGrabMode != ModelGrabMode.OneHand)
            {
                _oneHandGrab = new MerkabaDesignLibrary.OneHandGrab(
                    controllerPosition,
                    controllerRotation, _modelRoot.position,
                    _modelRoot.rotation, _modelRoot.localScale);
                _modelGrabActive = true;
                _modelGrabMode = ModelGrabMode.OneHand;
            }
            MerkabaDesignLibrary.ApplyOneHandTransform(_modelRoot,
                _oneHandGrab, controllerPosition, controllerRotation);
        }

        private void ContinueTwoHandModelGrab(Vector3 leftPosition,
            Quaternion leftRotation, Vector3 rightPosition,
            Quaternion rightRotation)
        {
            if (!TryBuildTwoHandFrame(leftPosition, leftRotation,
                    rightPosition, rightRotation, out Vector3 midpoint,
                    out Quaternion frame, out float separation))
                return;
            if (!_modelGrabActive || _modelGrabMode != ModelGrabMode.TwoHand)
            {
                _twoHandGrab = new MerkabaDesignLibrary.TwoHandGrab(midpoint,
                    frame, separation,
                    _modelRoot.position, _modelRoot.rotation,
                    _modelRoot.localScale);
                _modelGrabActive = true;
                _modelGrabMode = ModelGrabMode.TwoHand;
            }
            MerkabaDesignLibrary.ApplyTwoHandTransform(_modelRoot,
                _twoHandGrab, midpoint, frame, separation);
        }

        private void EndModelGrab()
        {
            _modelGrabActive = false;
            _modelGrabMode = ModelGrabMode.None;
        }

        internal static bool TryBuildTwoHandFrame(Vector3 leftPosition,
            Quaternion leftRotation, Vector3 rightPosition,
            Quaternion rightRotation, out Vector3 midpoint,
            out Quaternion frame, out float separation) =>
            MerkabaDesignLibrary.TryBuildTwoHandFrame(leftPosition,
                leftRotation, rightPosition, rightRotation, out midpoint,
                out frame, out separation);

        private bool TryBeginAnnotationPoseGrab(Ray ray,
            Vector3 controllerPosition, Quaternion controllerRotation)
        {
            if (_annotationMode != AnnotationMode.Move) return false;
            AnnotationRecord annotation = FindNearestAnnotation(ray);
            if (annotation == null) return false;
            _selectedAnnotationId = annotation.id;
            Vector3[] worldPoints = Array.ConvertAll(annotation.points,
                AnnotationWorldPoint);
            _annotationPoseGrab = new AnnotationPoseGrab(annotation.id,
                controllerPosition, controllerRotation, worldPoints);
            _annotationPoseGrabActive = true;
            _moveOriginalPoints = null;
            _moveHandleIndex = -1;
            RefreshAnnotationObjects();
            Status = $"6DoF editing {annotation.type} #{annotation.id}";
            return true;
        }

        private void ContinueAnnotationPoseGrab(Vector3 controllerPosition,
            Quaternion controllerRotation)
        {
            if (!_annotationPoseGrabActive) return;
            AnnotationRecord annotation = _annotations.Find(item =>
                item.id == _annotationPoseGrab.AnnotationId);
            if (annotation == null ||
                annotation.points.Length !=
                _annotationPoseGrab.WorldPoints.Length)
            {
                _annotationPoseGrabActive = false;
                return;
            }
            Quaternion deltaRotation = controllerRotation *
                Quaternion.Inverse(_annotationPoseGrab.ControllerRotation);
            for (int index = 0; index < annotation.points.Length; index++)
            {
                Vector3 world = controllerPosition + deltaRotation *
                    (_annotationPoseGrab.WorldPoints[index] -
                        _annotationPoseGrab.ControllerPosition);
                annotation.points[index] = WorldToScanPoint(world);
            }
            MarkAnnotationsDirty();
            UpdateAnnotationObject(annotation);
        }

        private void ApplyViewerFrame()
        {
            if (_modelRoot == null) return;
            Transform parent = _worldLocked
                ? RoomSpaceRoot.Instance?.transform
                : Camera.main?.transform;
            _modelRoot.SetParent(parent, true);
            Status = _worldLocked
                ? "GLB View world lock enabled"
                : "GLB View follows headset";
        }

        private async Task SetRoomAlignedAsync(bool aligned)
        {
            if (ExportMutationHeld) return;
            if (!aligned)
            {
                ++_alignmentRevision;
                ExitRoomAlignment();
                return;
            }
            if (_roomAligned || _alignmentPending || _modelRoot == null)
                return;
            if (!_packageSpatialBinding.HasValue ||
                !_packageSpatialBinding.Value.IsValid)
            {
                Status = "ALIGN 1:1 unavailable: package has no spatial binding";
                return;
            }
            RoomAnchorManager manager = RoomAnchorManager.Instance;
            if (manager == null)
            {
                Status = "ALIGN 1:1 unavailable: anchor service is missing";
                return;
            }

            int generation = _generation;
            int revision = ++_alignmentRevision;
            MerkabaSpatialBinding binding = _packageSpatialBinding.Value;
            _alignmentPending = true;
            Status = $"Localizing model anchor {binding.AnchorUuid:D}…";
            (Transform transform, bool owned)? localized = null;
            try
            {
                localized = await manager.LocalizeArtifactAnchorAsync(
                    binding.AnchorUuid);
                if (generation != _generation || revision !=
                    _alignmentRevision || !IsOpen || _modelRoot == null || ExportMutationHeld)
                {
                    if (localized.HasValue && localized.Value.owned &&
                        localized.Value.transform != null)
                        Destroy(localized.Value.transform.gameObject);
                    return;
                }
                if (!localized.HasValue || localized.Value.transform == null)
                {
                    Status = "ALIGN 1:1 failed: package anchor was not localized";
                    return;
                }

                Matrix4x4 local = ComposeAlignedModelLocal(
                    binding.AnchorFromPackage, _scanCenter);
                if (!TryDecomposeTransform(local, out Vector3 position,
                        out Quaternion rotation, out Vector3 scale))
                {
                    if (localized.Value.owned)
                        Destroy(localized.Value.transform.gameObject);
                    Status = "ALIGN 1:1 failed: invalid package transform";
                    return;
                }

                _savedPreviewWorldPosition = _modelRoot.position;
                _savedPreviewWorldRotation = _modelRoot.rotation;
                _savedPreviewScale = _modelRoot.localScale;
                _hasSavedPreviewTransform = true;
                _artifactAnchor = localized.Value.transform;
                _ownsArtifactAnchor = localized.Value.owned;
                _worldLocked = true;
                _modelRoot.SetParent(_artifactAnchor, false);
                _modelRoot.localPosition = position;
                _modelRoot.localRotation = rotation;
                _modelRoot.localScale = scale;
                _roomAligned = true;
                RefreshAnnotationObjects();
                Status = "GLB View aligned 1:1 on its persisted room anchor";
                Logger.Info($"Merkaba GLB View ALIGN 1:1 anchor=" +
                    $"{binding.AnchorUuid:D}, localOrigin={position}.");
            }
            catch (Exception exception)
            {
                if (localized.HasValue && localized.Value.owned &&
                    localized.Value.transform != null)
                    Destroy(localized.Value.transform.gameObject);
                Logger.Error("GLB View ALIGN 1:1 failed: " + exception);
                Status = "ALIGN 1:1 failed: " + exception.Message;
            }
            finally
            {
                if (generation == _generation && revision ==
                    _alignmentRevision)
                    _alignmentPending = false;
            }
        }

        private void ExitRoomAlignment()
        {
            _alignmentPending = false;
            if (_modelRoot == null)
            {
                _roomAligned = false;
                ReleaseArtifactAnchor();
                return;
            }
            bool wasAligned = _roomAligned;
            _roomAligned = false;
            ApplyViewerFrame();
            if (_hasSavedPreviewTransform)
            {
                _modelRoot.SetPositionAndRotation(_savedPreviewWorldPosition,
                    _savedPreviewWorldRotation);
                _modelRoot.localScale = _savedPreviewScale;
                _hasSavedPreviewTransform = false;
            }
            ReleaseArtifactAnchor();
            if (wasAligned)
            {
                RefreshAnnotationObjects();
                Status = "GLB View restored to model review";
            }
        }

        private void ReleaseArtifactAnchor()
        {
            if (_ownsArtifactAnchor && _artifactAnchor != null)
                Destroy(_artifactAnchor.gameObject);
            _artifactAnchor = null;
            _ownsArtifactAnchor = false;
        }

        internal static Matrix4x4 ComposeAlignedModelLocal(
            Matrix4x4 anchorFromPackage, Vector3 scanCenter) =>
            anchorFromPackage * Matrix4x4.Translate(scanCenter);

        // Existing design documents stay anchor-local. Only their presentation
        // follows the centered/scaled model, and ALIGN cancels this transform.
        internal static Matrix4x4 ComposeSessionDesignLocal(
            Matrix4x4 anchorFromPackage, Vector3 scanCenter) =>
            Matrix4x4.Translate(-scanCenter) * anchorFromPackage.inverse;

        internal static bool TryDecomposeTransform(Matrix4x4 matrix,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = matrix.GetColumn(3);
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);
            scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            rotation = Quaternion.identity;
            if (scale.x < 1e-6f || scale.y < 1e-6f || scale.z < 1e-6f ||
                !IsFinite(position) || !IsFinite(scale))
                return false;
            x /= scale.x;
            y /= scale.y;
            z /= scale.z;
            if (Mathf.Abs(Vector3.Dot(x, y)) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(x, z)) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(y, z)) > 1e-4f ||
                Vector3.Dot(Vector3.Cross(x, y), z) < 0.999f)
                return false;
            rotation = Quaternion.LookRotation(z, y);
            return IsFinite(new Vector3(rotation.x, rotation.y, rotation.z)) &&
                !float.IsNaN(rotation.w) && !float.IsInfinity(rotation.w);
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private void CreateBackdrop(Camera camera)
        {
            if (camera == null || _backdrop != null) return;
            _backdrop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _backdrop.name = "Merkaba GLB View Backdrop";
            Collider collider = _backdrop.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _backdrop.transform.SetParent(camera.transform, false);
            _backdrop.transform.localPosition = Vector3.zero;
            _backdrop.transform.localRotation = Quaternion.identity;
            _backdrop.transform.localScale = Vector3.one * 40f;
            _backdropMaterial = new Material(previewShader)
            {
                name = "Merkaba GLB View Backdrop",
                hideFlags = HideFlags.DontSave
            };
            ConfigureMaterial(_backdropMaterial, backdropColor, false);
            _backdropMaterial.renderQueue = (int)RenderQueue.Background;
            MeshRenderer renderer = _backdrop.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _backdropMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void ApplyPreviewOpacity()
        {
            if (_modelMaterial == null) return;
            bool visible = _previewOpacity > 0.001f;
            ConfigureMaterial(_modelMaterial,
                new Color(1f, 1f, 1f, _previewOpacity), true);
            if (_planViewEnabled)
            {
                Color plan = architecturalPlanColor;
                plan.a = _previewOpacity;
                _modelMaterial.SetColor(PlanColorId, plan);
                _modelMaterial.EnableKeyword(PlanKeyword);
            }
            else
                _modelMaterial.DisableKeyword(PlanKeyword);
            _modelMaterial.SetFloat(AlphaDitherId,
                _previewOpacity < 0.999f ? 1f : 0f);
            foreach (Tile tile in _tiles)
            {
                tile.Presentation?.ApplyStyle(_modelMaterial);
                MeshRenderer renderer = tile.Object != null
                    ? tile.Object.GetComponent<MeshRenderer>() : null;
                if (renderer != null) renderer.enabled = visible;
            }
        }

        internal static void ConfigureMaterial(Material material, Color color,
            bool opaque)
        {
            if (material == null) return;
            material.SetColor(BaseColorId, color);
            material.SetFloat(AlphaDitherId, 0f);
            material.SetFloat(SourceBlendId, (float)(opaque
                ? BlendMode.One : BlendMode.SrcAlpha));
            material.SetFloat(DestinationBlendId, (float)(opaque
                ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
            material.SetFloat(ZWriteId, opaque ? 1f : 0f);
            material.SetOverrideTag("RenderType", opaque
                ? "Opaque" : "Transparent");
            material.renderQueue = opaque
                ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
        }

        private void HandlePaintInput(Ray ray)
        {
            bool triggerDown = OVRInput.GetDown(
                OVRInput.Button.SecondaryIndexTrigger);
            bool triggerHeld = OVRInput.Get(
                OVRInput.Button.SecondaryIndexTrigger);
            bool triggerUp = OVRInput.GetUp(
                OVRInput.Button.SecondaryIndexTrigger);

            if (_paintEngine == null || !_paintEngine.IsOpen)
            {
                CancelPaintStroke();
                SetAnnotationHitPreview(false, default);
                Status = "Open an anchored scan session before painting";
                return;
            }

            if (_paintTool == MerkabaArtifactPaintTool.Eyedropper)
            {
                CancelPaintStroke();
                bool paint = _paintEngine.TrySample(ray,
                    out MerkabaPaintEngine.PaintHit paintHit);
                bool model = TryHitModel(ray, out ModelHit modelHit,
                    triggerDown);
                bool usePaint = paint && (!model || paintHit.Along <
                    Vector3.Distance(ray.origin, modelHit.Point));
                if (usePaint)
                    SetAnnotationHitPreview(true, new ModelHit(
                        paintHit.Point, -ray.direction));
                else
                    SetAnnotationHitPreview(model, modelHit);
                if (!triggerDown) return;
                if (usePaint)
                    PaintColor = paintHit.Color;
                else if (model && modelHit.HasColor)
                    PaintColor = modelHit.Color;
                else
                {
                    Status = "No displayed color under pointer";
                    return;
                }
                Status = "Sampled displayed color";
                return;
            }

            if (_paintTool == MerkabaArtifactPaintTool.Erase)
            {
                CancelPaintStroke();
                bool paint = _paintEngine.TrySample(ray,
                    out MerkabaPaintEngine.PaintHit paintHit);
                Vector3 center = paint
                    ? paintHit.Point
                    : MerkabaPaintEngine.SpatialBrushPoint(ray);
                SetAnnotationHitPreview(true, new ModelHit(center,
                    -ray.direction));
                if (triggerHeld && Time.unscaledTime >= _nextPaintErase)
                {
                    _nextPaintErase = Time.unscaledTime + PaintEraseInterval;
                    int removed = _paintEngine.EraseSphere(center,
                        _paintWidth);
                    Status = removed > 0
                        ? $"Erased {removed} local paint dabs"
                        : "No paint inside eraser";
                }
                return;
            }

            bool surfaceTool = PaintToolUsesSurface(_paintTool);
            ModelHit surfaceHit = default;
            bool hasSurfaceHit = surfaceTool &&
                TryHitModel(ray, out surfaceHit);
            if (!_paintEngine.HasActiveStroke)
            {
                if (surfaceTool)
                    SetAnnotationHitPreview(hasSurfaceHit, surfaceHit);
                else
                    SetAnnotationHitPreview(true, new ModelHit(
                        MerkabaPaintEngine.SpatialBrushPoint(ray),
                        -ray.direction));
            }
            else SetAnnotationHitPreview(false, default);

            if (triggerDown)
            {
                if (surfaceTool && !hasSurfaceHit)
                {
                    Status = "No exported surface under paint pointer";
                    return;
                }
                BeginPaintStroke(ray, surfaceHit);
            }
            if (triggerHeld && _paintEngine.HasActiveStroke)
                ContinuePaintStroke(ray);
            if (triggerUp && _paintEngine.HasActiveStroke)
                CompletePaintStroke();
        }

        private void BeginPaintStroke(Ray ray, ModelHit surfaceHit)
        {
            CancelPaintStroke();
            _paintEngine.BeginStroke(DesignTool(_paintTool),
                new MerkabaPaintSettings(_paintColor, _paintColor.a,
                    _paintFlow, _paintHardness, _paintSaturation, _paintWidth,
                    _paintShape));
            _hasPaintSample = false;
            if (_paintTool == MerkabaArtifactPaintTool.Line)
            {
                _lineStart = SurfacePaintPoint(surfaceHit);
                _lineNormal = surfaceHit.Normal;
                _lastPaintPoint = _lineStart;
                _paintEngine.SetLine(_lineStart, _lineStart, _lineNormal,
                    true);
                _hasPaintSample = true;
                return;
            }
            if (PaintToolUsesSurface(_paintTool))
            {
                Vector3 point = SurfacePaintPoint(surfaceHit);
                _paintEngine.AddSample(point, surfaceHit.Normal, true);
                _lastPaintRay = ray;
                _lastPaintPoint = point;
                _hasPaintSample = true;
                return;
            }
            if (_paintTool == MerkabaArtifactPaintTool.SpatialBrush)
            {
                Vector3 point = MerkabaPaintEngine.SpatialBrushPoint(ray);
                _paintEngine.AddSample(point, Vector3.zero, false);
                _lastPaintPoint = point;
                _hasPaintSample = true;
            }
        }

        private void ContinuePaintStroke(Ray ray)
        {
            if (_paintEngine == null || !_paintEngine.HasActiveStroke) return;
            if (_paintTool == MerkabaArtifactPaintTool.Line)
            {
                if (!TryHitModel(ray, out ModelHit hit)) return;
                _lastPaintPoint = SurfacePaintPoint(hit);
                _lineNormal = Vector3.Slerp(_lineNormal, hit.Normal, 0.5f);
                _paintEngine.SetLine(_lineStart, _lastPaintPoint,
                    _lineNormal, true);
                return;
            }
            if (PaintToolUsesSurface(_paintTool))
            {
                AppendProjectedSurfaceSamples(ray);
                return;
            }
            Vector3 spatialPoint = MerkabaPaintEngine.SpatialBrushPoint(ray);
            if (_paintTool == MerkabaArtifactPaintTool.Spray)
            {
                _paintEngine.AddSpray(spatialPoint, ray.direction,
                    Time.unscaledDeltaTime, _sprayDensity, _sprayScatter);
                return;
            }
            AppendSpatialSamples(spatialPoint);
        }

        private Vector3 SurfacePaintPoint(ModelHit hit) => hit.Point +
            hit.Normal.normalized * PaintSurfaceOffset;

        private void CompletePaintStroke()
        {
            if (_paintEngine == null || !_paintEngine.HasActiveStroke) return;
            bool committed = _paintEngine.CommitStroke();
            _hasPaintSample = false;
            Status = committed ? $"Added {_paintTool} stroke" :
                "Paint stroke was too short";
        }

        private void CancelPaintStroke()
        {
            _paintEngine?.CancelStroke();
            _paintInputSamples.Clear();
            _hasPaintSample = false;
        }

        private void AppendProjectedSurfaceSamples(Ray currentRay)
        {
            if (!TryHitModel(currentRay, out ModelHit currentHit))
            {
                _hasPaintSample = false;
                return;
            }
            Vector3 currentPoint = SurfacePaintPoint(currentHit);
            if (!_hasPaintSample)
            {
                _paintEngine.AddSample(currentPoint, currentHit.Normal, true);
                _lastPaintRay = currentRay;
                _lastPaintPoint = currentPoint;
                _hasPaintSample = true;
                return;
            }
            float spacing = MerkabaPaintEngine.SurfaceSampleSpacing(
                _paintWidth);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(
                _lastPaintPoint, currentPoint) / spacing));
            _paintInputSamples.Clear();
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                Vector3 origin = Vector3.Lerp(_lastPaintRay.origin,
                    currentRay.origin, t);
                Vector3 direction = Vector3.Slerp(_lastPaintRay.direction,
                    currentRay.direction, t).normalized;
                if (!TryHitModel(new Ray(origin, direction),
                        out ModelHit hit)) continue;
                _paintInputSamples.Add(new MerkabaPaintEngine.PaintInputSample(
                    SurfacePaintPoint(hit), hit.Normal, true));
            }
            _paintEngine.AddSamples(_paintInputSamples);
            _lastPaintRay = currentRay;
            _lastPaintPoint = currentPoint;
        }

        private void AppendSpatialSamples(Vector3 currentPoint)
        {
            if (!_hasPaintSample)
            {
                _paintEngine.AddSample(currentPoint, Vector3.zero, false);
                _lastPaintPoint = currentPoint;
                _hasPaintSample = true;
                return;
            }
            float spacing = MerkabaPaintEngine.SurfaceSampleSpacing(
                _paintWidth);
            int steps = Mathf.CeilToInt(Vector3.Distance(_lastPaintPoint,
                currentPoint) / spacing);
            if (steps <= 0) return;
            _paintInputSamples.Clear();
            for (int step = 1; step <= steps; step++)
                _paintInputSamples.Add(new MerkabaPaintEngine.PaintInputSample(
                    Vector3.Lerp(_lastPaintPoint, currentPoint,
                        step / (float)steps), Vector3.zero, false));
            _paintEngine.AddSamples(_paintInputSamples);
            _lastPaintPoint = currentPoint;
        }

        private static bool PaintToolUsesSurface(
            MerkabaArtifactPaintTool tool) =>
            tool is MerkabaArtifactPaintTool.Brush or
                MerkabaArtifactPaintTool.SurfaceBrush or
                MerkabaArtifactPaintTool.Line;

        private static MerkabaDesignTool DesignTool(
            MerkabaArtifactPaintTool tool) => tool switch
            {
                MerkabaArtifactPaintTool.Brush => MerkabaDesignTool.Brush,
                MerkabaArtifactPaintTool.SurfaceBrush =>
                    MerkabaDesignTool.SurfaceBrush,
                MerkabaArtifactPaintTool.SpatialBrush =>
                    MerkabaDesignTool.SpatialBrush,
                MerkabaArtifactPaintTool.Spray => MerkabaDesignTool.Spray,
                MerkabaArtifactPaintTool.Line => MerkabaDesignTool.Line,
                _ => throw new ArgumentOutOfRangeException(nameof(tool))
            };

        private static bool IsPaintAnnotation(AnnotationRecord annotation) =>
            annotation != null && annotation.type != null &&
            annotation.type.StartsWith("paint-", StringComparison.Ordinal);

        private void UpdateAnnotationHitPreview(Ray ray)
        {
            bool needsSurface = _annotationMode is AnnotationMode.Point or
                AnnotationMode.Line or AnnotationMode.Plane;
            _hasModelHit = needsSurface && !_annotationDrag.Active &&
                TryHitModel(ray, out _latestModelHit);
            SetAnnotationHitPreview(_hasModelHit, _latestModelHit);
        }

        private void SetAnnotationHitPreview(bool visible, ModelHit hit)
        {
            if (!visible)
            {
                _hasModelHit = false;
                if (_annotationHitPreview != null)
                    _annotationHitPreview.SetActive(false);
                return;
            }
            if (_annotationHitPreview == null)
            {
                _pointMarkerMesh ??= CreatePointMarkerMesh();
                _annotationHitPreview = new GameObject(
                    "Annotation Surface Hit Preview");
                _annotationHitPreview.transform.SetParent(_modelRoot, false);
                var filter = _annotationHitPreview.AddComponent<MeshFilter>();
                filter.sharedMesh = _pointMarkerMesh;
                var renderer =
                    _annotationHitPreview.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _annotationMaterial;
                ConfigureRenderer(renderer,
                    new Color(0.15f, 0.95f, 1f, 0.55f));
            }
            _annotationHitPreview.SetActive(true);
            _annotationHitPreview.transform.localPosition =
                WorldToScanPoint(hit.Point) - _scanCenter;
            _annotationHitPreview.transform.localRotation =
                Quaternion.Inverse(_modelRoot.rotation) *
                Quaternion.FromToRotation(Vector3.forward, hit.Normal);
            _annotationHitPreview.transform.localScale = Vector3.one *
                AnnotationPointRadius;
        }

        private void BeginAnnotationDrag(Ray ray)
        {
            if (!_hasModelHit && !TryHitModel(ray, out _latestModelHit))
            {
                Status = "No exported surface under pointer";
                return;
            }
            ModelHit hit = _latestModelHit;
            _hasModelHit = true;
            SetAnnotationHitPreview(false, default);
            Vector3 normal = hit.Normal.normalized;
            Camera camera = Camera.main;
            Vector3 tangentU = Vector3.ProjectOnPlane(camera != null
                ? camera.transform.right : Vector3.right, normal);
            if (tangentU.sqrMagnitude < 1e-6f)
                tangentU = Vector3.Cross(normal, Vector3.up);
            if (tangentU.sqrMagnitude < 1e-6f)
                tangentU = Vector3.Cross(normal, Vector3.forward);
            tangentU.Normalize();
            Vector3 tangentV = Vector3.Cross(normal, tangentU).normalized;
            _annotationDrag = new AnnotationDrag(_annotationMode, hit.Point,
                new Plane(normal, hit.Point), tangentU, tangentV);
            UpdateAnnotationDraft(hit.Point);
            Status = $"Drag to size {AnnotationModeText.ToLowerInvariant()}";
        }

        private void ContinueAnnotationDrag(Ray ray)
        {
            if (!_annotationDrag.Active) return;
            Vector3 point;
            if (_annotationDrag.Mode == AnnotationMode.Line &&
                TryHitModel(ray, out ModelHit hit))
                point = hit.Point;
            else if (_annotationDrag.Surface.Raycast(ray, out float distance))
                point = ray.GetPoint(distance);
            else
                return;
            _annotationDrag.CurrentWorld = point;
            UpdateAnnotationDraft(point);
        }

        private void CompleteAnnotationDrag(Ray ray)
        {
            if (!_annotationDrag.Active) return;
            ContinueAnnotationDrag(ray);
            AnnotationDrag drag = _annotationDrag;
            if (Vector3.Distance(drag.StartWorld, drag.CurrentWorld) <
                MinimumAnnotationDrag)
            {
                CancelAnnotationDrag();
                Status = "Drag was too short";
                return;
            }

            Vector3[] worldPoints;
            if (drag.Mode == AnnotationMode.Line)
                worldPoints = new[] { drag.StartWorld, drag.CurrentWorld };
            else
            {
                Vector3 delta = drag.CurrentWorld - drag.StartWorld;
                float u = Vector3.Dot(delta, drag.TangentU);
                float v = Vector3.Dot(delta, drag.TangentV);
                if (Mathf.Abs(u) < MinimumAnnotationDrag * 0.25f ||
                    Mathf.Abs(v) < MinimumAnnotationDrag * 0.25f)
                {
                    CancelAnnotationDrag();
                    Status = "Plane needs width and height";
                    return;
                }
                worldPoints = new[]
                {
                    drag.StartWorld,
                    drag.StartWorld + drag.TangentU * u,
                    drag.StartWorld + drag.TangentU * u + drag.TangentV * v,
                    drag.StartWorld + drag.TangentV * v
                };
            }

            var annotation = new AnnotationRecord
            {
                id = _nextAnnotationId++,
                type = drag.Mode.ToString().ToLowerInvariant(),
                note = string.Empty,
                points = Array.ConvertAll(worldPoints, WorldToScanPoint)
            };
            CancelAnnotationDrag();
            _annotations.Add(annotation);
            _selectedAnnotationId = annotation.id;
            MarkAnnotationsDirty();
            RefreshAnnotationObjects();
            Status = $"Added {annotation.type} #{annotation.id}";
        }

        private Vector3 WorldToScanPoint(Vector3 worldPoint) =>
            _modelRoot.InverseTransformPoint(worldPoint) + _scanCenter;

        private void CancelTransientInput()
        {
            EndModelGrab();
            _designLibrary?.EndGrab(true);
            CancelPaintStroke();
            _annotationPoseGrabActive = false;
            _moveOriginalPoints = null;
            _moveHandleIndex = -1;
            SetAnnotationHitPreview(false, default);
            CancelAnnotationDrag();
        }

        private void SelectAnnotation(Ray ray)
        {
            AnnotationRecord selected = FindNearestAnnotation(ray);
            if (selected == null)
            {
                Status = "No annotation under pointer";
                return;
            }
            _selectedAnnotationId = selected.id;
            RefreshAnnotationObjects();
            Status = $"Selected {selected.type} #{selected.id}";
            BeginNoteEdit();
        }

        private void AddPointAnnotation(Ray ray)
        {
            if (!_hasModelHit && !TryHitModel(ray, out _latestModelHit))
            {
                Status = "No exported surface under pointer";
                return;
            }
            ModelHit hit = _latestModelHit;
            var annotation = new AnnotationRecord
            {
                id = _nextAnnotationId++,
                type = "point",
                note = string.Empty,
                points = new[] { WorldToScanPoint(hit.Point) }
            };
            _annotations.Add(annotation);
            _selectedAnnotationId = annotation.id;
            MarkAnnotationsDirty();
            RefreshAnnotationObjects();
            Status = $"Added {annotation.type} #{annotation.id}";
        }

        private void BeginMove(Ray ray)
        {
            AnnotationRecord selected = FindNearestAnnotation(ray);
            if (selected == null)
            {
                Status = "Point at an annotation or one of its handles";
                return;
            }
            _selectedAnnotationId = selected.id;
            _moveOriginalPoints = (Vector3[])selected.points.Clone();
            _moveHandleIndex = FindNearestAnnotationHandle(ray, selected);
            Vector3 grabWorld = _moveHandleIndex >= 0
                ? AnnotationWorldPoint(selected.points[_moveHandleIndex])
                : ray.GetPoint(AnnotationAlongRay(ray, selected));
            Camera camera = Camera.main;
            Vector3 normal = camera != null ? camera.transform.forward :
                ray.direction;
            _movePlane = new Plane(normal, grabWorld);
            _moveStart = TryResolveMoveTarget(ray, out Vector3 target)
                ? target : grabWorld;
            RefreshAnnotationObjects();
            Status = _moveHandleIndex >= 0
                ? $"Editing {selected.type} #{selected.id} handle " +
                  $"{_moveHandleIndex + 1}"
                : $"Moving {selected.type} #{selected.id}";
        }

        private void ContinueMove(Ray ray)
        {
            if (_moveOriginalPoints == null ||
                !TryResolveMoveTarget(ray, out Vector3 target)) return;
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected == null) return;
            if (_moveHandleIndex >= 0)
            {
                Vector3 targetScan = WorldToScanPoint(target);
                if (selected.type == "plane" &&
                    _moveOriginalPoints.Length >= 4)
                    selected.points = ResizePlaneCorner(_moveOriginalPoints,
                        _moveHandleIndex, targetScan, MinimumAnnotationDrag);
                else
                    selected.points[_moveHandleIndex] = targetScan;
                MarkAnnotationsDirty();
                UpdateAnnotationObject(selected);
                return;
            }
            Vector3 deltaWorld = target - _moveStart;
            Vector3 deltaScan = _modelRoot.InverseTransformVector(deltaWorld);
            for (int index = 0; index < selected.points.Length; index++)
                selected.points[index] = _moveOriginalPoints[index] + deltaScan;
            MarkAnnotationsDirty();
            UpdateAnnotationObject(selected);
        }

        private bool TryResolveMoveTarget(Ray ray, out Vector3 target)
        {
            if (TryHitModel(ray, out ModelHit hit))
            {
                target = hit.Point;
                return true;
            }
            if (_movePlane.Raycast(ray, out float along))
            {
                target = ray.GetPoint(along);
                return true;
            }
            target = default;
            return false;
        }

        private float AnnotationAlongRay(Ray ray, AnnotationRecord annotation)
        {
            return TryMeasureAnnotation(ray, annotation, out _, out float along)
                ? along : Mathf.Max(0.05f, Vector3.Dot(
                    AnnotationWorldPoint(annotation.points[0]) - ray.origin,
                    ray.direction));
        }

        private int FindNearestAnnotationHandle(Ray ray,
            AnnotationRecord annotation)
        {
            if (annotation?.points == null) return -1;
            int best = -1;
            float bestDistance = AnnotationPickRadius;
            float bestAlong = float.PositiveInfinity;
            for (int index = 0; index < annotation.points.Length; index++)
            {
                float distance = RayPointDistance(ray,
                    AnnotationWorldPoint(annotation.points[index]),
                    out float along);
                if (along <= 0f || distance > bestDistance ||
                    (Mathf.Approximately(distance, bestDistance) &&
                     along >= bestAlong)) continue;
                best = index;
                bestDistance = distance;
                bestAlong = along;
            }
            return best;
        }

        internal static Vector3[] ResizePlaneCorner(Vector3[] original,
            int corner, Vector3 target, float minimumSize)
        {
            if (original == null || original.Length < 4 ||
                (uint)corner >= 4u)
                throw new ArgumentException("A plane needs four corners.",
                    nameof(original));
            Vector3 uAxis = (original[1] - original[0]).normalized;
            Vector3 vAxis = (original[3] - original[0]).normalized;
            if (uAxis.sqrMagnitude < 0.99f || vAxis.sqrMagnitude < 0.99f)
                return (Vector3[])original.Clone();
            int opposite = (corner + 2) & 3;
            Vector3 fixedPoint = original[opposite];
            float du = Vector3.Dot(target - fixedPoint, uAxis);
            float dv = Vector3.Dot(target - fixedPoint, vAxis);
            float expectedU = Vector3.Dot(original[corner] - fixedPoint,
                uAxis);
            float expectedV = Vector3.Dot(original[corner] - fixedPoint,
                vAxis);
            du = ClampSignedMagnitude(du, expectedU, minimumSize);
            dv = ClampSignedMagnitude(dv, expectedV, minimumSize);
            Vector3 selectedPoint = fixedPoint + uAxis * du + vAxis * dv;
            var result = (Vector3[])original.Clone();
            result[corner] = selectedPoint;
            bool selectedU = corner == 1 || corner == 2;
            bool selectedV = corner >= 2;
            result[RectangleCornerIndex(selectedU, !selectedV)] =
                fixedPoint + uAxis * du;
            result[RectangleCornerIndex(!selectedU, selectedV)] =
                fixedPoint + vAxis * dv;
            return result;
        }

        private static int RectangleCornerIndex(bool u, bool v) =>
            v ? (u ? 2 : 3) : (u ? 1 : 0);

        private static float ClampSignedMagnitude(float value,
            float expectedSign, float minimum)
        {
            float sign = expectedSign < 0f ? -1f : 1f;
            return sign * Mathf.Max(minimum, value * sign);
        }

        private bool TryHitModel(Ray ray, out ModelHit modelHit,
            bool sampleColor = false)
        {
            float nearest = float.PositiveInfinity;
            modelHit = default;
            bool found = false;
            foreach (Tile tile in _tiles)
            {
                if (tile.Object == null ||
                    !TransformBounds(_modelRoot,
                        Centered(tile.Bounds)).IntersectRay(ray,
                        out float boundsDistance) ||
                    boundsDistance > ModelRayDistance)
                    continue;
                EnsureTileCollider(tile);
                if (tile.Collider == null || !tile.Collider.Raycast(ray,
                        out RaycastHit hit, ModelRayDistance) ||
                    hit.distance >= nearest) continue;
                nearest = hit.distance;
                Color color = Color.white;
                bool hasColor = sampleColor && TryInterpolateVertexColor(
                    tile.Mesh, hit, out color);
                if (hasColor && tile.Presentation != null)
                    color *= tile.Presentation.SampleCapture(hit.triangleIndex, hit.textureCoord);
                modelHit = new ModelHit(hit.point, hit.normal, color,
                    hasColor);
                found = true;
            }
            return found;
        }

        private static bool TryInterpolateVertexColor(Mesh mesh,
            RaycastHit hit, out Color color)
        {
            color = Color.white;
            if (mesh == null || hit.triangleIndex < 0) return false;
            int[] indices = mesh.triangles;
            Color32[] colors = mesh.colors32;
            int triangle = hit.triangleIndex * 3;
            if (triangle + 2 >= indices.Length || colors.Length == 0)
                return false;
            int i0 = indices[triangle];
            int i1 = indices[triangle + 1];
            int i2 = indices[triangle + 2];
            if ((uint)i0 >= colors.Length || (uint)i1 >= colors.Length ||
                (uint)i2 >= colors.Length) return false;
            Vector3 barycentric = hit.barycentricCoordinate;
            color = (Color)colors[i0] * barycentric.x +
                (Color)colors[i1] * barycentric.y +
                (Color)colors[i2] * barycentric.z;
            return true;
        }

        private static void EnsureTileCollider(Tile tile)
        {
            if (tile.Collider != null || tile.Object == null ||
                tile.Mesh == null)
                return;
            tile.Collider = tile.Object.AddComponent<MeshCollider>();
            tile.Collider.sharedMesh = tile.Mesh;
        }

        private AnnotationRecord FindNearestAnnotation(Ray ray)
        {
            AnnotationRecord best = null;
            float bestDistance = AnnotationPickRadius;
            float bestAlong = float.PositiveInfinity;
            foreach (AnnotationRecord annotation in _annotations)
            {
                if (!TryMeasureAnnotation(ray, annotation, out float distance,
                        out float along) || distance > bestDistance ||
                    (Mathf.Approximately(distance, bestDistance) &&
                     along >= bestAlong)) continue;
                bestDistance = distance;
                bestAlong = along;
                best = annotation;
            }
            return best;
        }

        private bool TryMeasureAnnotation(Ray ray, AnnotationRecord annotation,
            out float distance, out float along)
        {
            distance = float.PositiveInfinity;
            along = float.PositiveInfinity;
            Vector3[] points = annotation.points;
            if (points == null || points.Length == 0) return false;
            if (annotation.type == "point")
            {
                distance = RayPointDistance(ray,
                    AnnotationWorldPoint(points[0]), out along);
                return along > 0f;
            }

            if (annotation.type == "plane" && points.Length >= 3)
            {
                Vector3 origin = AnnotationWorldPoint(points[0]);
                for (int index = 1; index + 1 < points.Length; index++)
                {
                    if (!RayTriangleDistance(ray, origin,
                            AnnotationWorldPoint(points[index]),
                            AnnotationWorldPoint(points[index + 1]),
                            out float triangleAlong) || triangleAlong >= along)
                        continue;
                    distance = 0f;
                    along = triangleAlong;
                }
                if (distance == 0f) return true;
            }

            int segmentCount = annotation.type == "plane"
                ? points.Length : points.Length - 1;
            for (int index = 0; index < segmentCount; index++)
            {
                Vector3 start = AnnotationWorldPoint(points[index]);
                Vector3 end = AnnotationWorldPoint(points[(index + 1) %
                    points.Length]);
                float candidate = RaySegmentDistance(ray, start, end,
                    out float candidateAlong);
                if (candidateAlong <= 0f || candidate > distance) continue;
                distance = candidate;
                along = candidateAlong;
            }
            return along < float.PositiveInfinity;
        }

        private Vector3 AnnotationWorldPoint(Vector3 scanPoint) =>
            _modelRoot.TransformPoint(scanPoint - _scanCenter);

        internal static float RayPointDistance(Ray ray, Vector3 point,
            out float along)
        {
            along = Mathf.Max(0f, Vector3.Dot(point - ray.origin,
                ray.direction));
            return Vector3.Distance(point, ray.GetPoint(along));
        }

        internal static float RaySegmentDistance(Ray ray, Vector3 start,
            Vector3 end, out float along)
        {
            Vector3 segment = end - start;
            float segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared < 1e-12f)
                return RayPointDistance(ray, start, out along);
            Vector3 originDelta = ray.origin - start;
            float raySegment = Vector3.Dot(ray.direction, segment);
            float rayOrigin = Vector3.Dot(ray.direction, originDelta);
            float segmentOrigin = Vector3.Dot(segment, originDelta);
            float denominator = segmentLengthSquared - raySegment * raySegment;
            float segmentT = denominator > 1e-12f
                ? Mathf.Clamp01((segmentOrigin - raySegment * rayOrigin) /
                    denominator)
                : 0f;
            along = Mathf.Max(0f, raySegment * segmentT - rayOrigin);
            segmentT = Mathf.Clamp01((segmentOrigin + raySegment * along) /
                segmentLengthSquared);
            along = Mathf.Max(0f, raySegment * segmentT - rayOrigin);
            return Vector3.Distance(ray.GetPoint(along),
                start + segment * segmentT);
        }

        internal static bool RayTriangleDistance(Ray ray, Vector3 a, Vector3 b,
            Vector3 c, out float along)
        {
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 cross = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < 1e-8f)
            {
                along = 0f;
                return false;
            }
            float inverse = 1f / determinant;
            Vector3 offset = ray.origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < 0f || u > 1f)
            {
                along = 0f;
                return false;
            }
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f)
            {
                along = 0f;
                return false;
            }
            along = Vector3.Dot(edge2, q) * inverse;
            return along > 0f;
        }

        private AnnotationRecord FindSelectedAnnotation() =>
            _annotations.Find(item => item.id == _selectedAnnotationId);

        private void RefreshAnnotationObjects()
        {
            if (_annotationRoot == null || _annotationMaterial == null) return;
            for (int index = _annotationRoot.childCount - 1; index >= 0; index--)
                Destroy(_annotationRoot.GetChild(index).gameObject);
            _annotationObjects.Clear();
            DestroyAnnotationMeshes();
            foreach (AnnotationRecord annotation in _annotations)
                CreateAnnotationObject(annotation);
        }

        private void CreateAnnotationObject(AnnotationRecord annotation)
        {
            Vector3[] points = annotation.points;
            if (points == null || points.Length == 0) return;
            bool selected = annotation.id == _selectedAnnotationId;
            bool paint = IsPaintAnnotation(annotation);
            Color outlineColor = paint && annotation.styled
                ? annotation.color
                : selected
                    ? new Color(0.15f, 0.95f, 1f, 0.98f)
                    : new Color(1f, 0.35f, 0.05f, 0.82f);
            var visual = new GameObject($"{annotation.type} {annotation.id}");
            visual.transform.SetParent(_annotationRoot, false);
            _annotationObjects[annotation.id] = visual;

            if (annotation.type == "point")
            {
                _pointMarkerMesh ??= CreatePointMarkerMesh();
                visual.transform.localPosition = points[0] - _scanCenter;
                visual.transform.localScale = Vector3.one *
                    AnnotationPointRadius;
                var filter = visual.AddComponent<MeshFilter>();
                filter.sharedMesh = _pointMarkerMesh;
                var renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _annotationMaterial;
                ConfigureRenderer(renderer, outlineColor);
                return;
            }

            if (annotation.type == "plane" && points.Length >= 4)
            {
                Mesh mesh = CreatePlaneMesh(points);
                _annotationMeshes.Add(mesh);
                var filter = visual.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _annotationPlaneMaterial;
                Color fill = outlineColor;
                fill.a = AnnotationPlaneAlpha;
                ConfigureRenderer(renderer, fill);
            }

            var line = visual.AddComponent<LineRenderer>();
            line.sharedMaterial = _annotationMaterial;
            line.startColor = line.endColor = Color.white;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.startWidth = line.endWidth = paint && annotation.width > 0f
                ? annotation.width : AnnotationLineWidth;
            line.positionCount = annotation.type == "plane"
                ? points.Length + 1 : points.Length;
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                line.SetPosition(pointIndex, points[pointIndex] - _scanCenter);
            if (annotation.type == "plane")
                line.SetPosition(points.Length, points[0] - _scanCenter);
            var lineProperties = new MaterialPropertyBlock();
            lineProperties.SetColor(BaseColorId, outlineColor);
            line.SetPropertyBlock(lineProperties);
            if (selected && !paint) CreateAnnotationHandles(visual, points);
        }

        private void CreateAnnotationHandles(GameObject visual,
            Vector3[] points)
        {
            _pointMarkerMesh ??= CreatePointMarkerMesh();
            for (int index = 0; index < points.Length; index++)
            {
                var handle = new GameObject("Handle " + index);
                handle.transform.SetParent(visual.transform, false);
                handle.transform.localPosition = points[index] - _scanCenter;
                handle.transform.localScale = Vector3.one *
                    AnnotationHandleRadius;
                var filter = handle.AddComponent<MeshFilter>();
                filter.sharedMesh = _pointMarkerMesh;
                var renderer = handle.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _annotationMaterial;
                ConfigureRenderer(renderer,
                    new Color(0.15f, 0.95f, 1f, 0.92f));
            }
        }

        private void UpdateAnnotationObject(AnnotationRecord annotation)
        {
            if (!_annotationObjects.TryGetValue(annotation.id,
                    out GameObject visual) || visual == null)
            {
                RefreshAnnotationObjects();
                return;
            }
            Vector3[] points = annotation.points;
            if (annotation.type == "point")
            {
                visual.transform.localPosition = points[0] - _scanCenter;
                return;
            }
            LineRenderer line = visual.GetComponent<LineRenderer>();
            if (line != null)
            {
                for (int index = 0; index < points.Length; index++)
                    line.SetPosition(index, points[index] - _scanCenter);
                if (annotation.type == "plane")
                    line.SetPosition(points.Length, points[0] - _scanCenter);
            }
            UpdateAnnotationHandles(visual, points);
            if (annotation.type != "plane") return;
            Mesh mesh = visual.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return;
            mesh.vertices = Array.ConvertAll(points,
                point => point - _scanCenter);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void UpdateAnnotationHandles(GameObject visual,
            Vector3[] points)
        {
            int count = Mathf.Min(visual.transform.childCount, points.Length);
            for (int index = 0; index < count; index++)
                visual.transform.GetChild(index).localPosition =
                    points[index] - _scanCenter;
        }

        private Mesh CreatePlaneMesh(Vector3[] scanPoints)
        {
            var mesh = new Mesh
            {
                name = "Merkaba Annotation Plane",
                vertices = Array.ConvertAll(scanPoints,
                    point => point - _scanCenter),
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
                colors32 = new[]
                {
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255)
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePointMarkerMesh()
        {
            var mesh = new Mesh
            {
                name = "Merkaba Annotation Point",
                vertices = new[]
                {
                    Vector3.right, Vector3.left, Vector3.up, Vector3.down,
                    Vector3.forward, Vector3.back
                },
                triangles = new[]
                {
                    2, 0, 4, 2, 4, 1, 2, 1, 5, 2, 5, 0,
                    3, 4, 0, 3, 1, 4, 3, 5, 1, 3, 0, 5
                },
                colors32 = new[]
                {
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255),
                    new Color32(255, 255, 255, 255)
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ConfigureRenderer(Renderer renderer, Color color)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(properties);
        }

        private void DestroyAnnotationMeshes()
        {
            foreach (Mesh mesh in _annotationMeshes)
                if (mesh != null) Destroy(mesh);
            _annotationMeshes.Clear();
        }

        private void UpdateAnnotationDraft(Vector3 currentWorld)
        {
            if (!_annotationDrag.Active) return;
            if (_annotationDraftObject == null)
            {
                _annotationDraftObject = new GameObject("Annotation Draft");
                _annotationDraftLine =
                    _annotationDraftObject.AddComponent<LineRenderer>();
                _annotationDraftLine.sharedMaterial = _annotationMaterial;
                _annotationDraftLine.startColor = _annotationDraftLine.endColor =
                    Color.white;
                _annotationDraftLine.useWorldSpace = true;
                _annotationDraftLine.alignment = LineAlignment.View;
                _annotationDraftLine.numCapVertices = 3;
                _annotationDraftLine.startWidth =
                    _annotationDraftLine.endWidth = AnnotationLineWidth *
                    Mathf.Max(_modelRoot.lossyScale.x, 1e-5f);
                var properties = new MaterialPropertyBlock();
                properties.SetColor(BaseColorId,
                    new Color(0.15f, 0.95f, 1f, 0.95f));
                _annotationDraftLine.SetPropertyBlock(properties);
                if (_annotationDrag.Mode == AnnotationMode.Plane)
                {
                    _annotationDraftMesh = new Mesh
                    {
                        name = "Merkaba Annotation Plane Draft"
                    };
                    var filter =
                        _annotationDraftObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = _annotationDraftMesh;
                    var renderer =
                        _annotationDraftObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = _annotationPlaneMaterial;
                    ConfigureRenderer(renderer,
                        new Color(0.15f, 0.95f, 1f,
                            AnnotationPlaneAlpha));
                }
            }

            if (_annotationDrag.Mode == AnnotationMode.Line)
            {
                _annotationDraftLine.positionCount = 2;
                _annotationDraftLine.SetPosition(0,
                    _annotationDrag.StartWorld);
                _annotationDraftLine.SetPosition(1, currentWorld);
                return;
            }

            Vector3[] points = DragPlanePoints(_annotationDrag, currentWorld);
            _annotationDraftLine.positionCount = 5;
            for (int index = 0; index < 4; index++)
                _annotationDraftLine.SetPosition(index, points[index]);
            _annotationDraftLine.SetPosition(4, points[0]);
            _annotationDraftMesh.vertices = points;
            _annotationDraftMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _annotationDraftMesh.colors32 = new[]
            {
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255)
            };
            _annotationDraftMesh.RecalculateNormals();
            _annotationDraftMesh.RecalculateBounds();
        }

        private static Vector3[] DragPlanePoints(AnnotationDrag drag,
            Vector3 currentWorld)
            => SurfaceRectangle(drag.StartWorld, currentWorld,
                drag.TangentU, drag.TangentV);

        internal static Vector3[] SurfaceRectangle(Vector3 start,
            Vector3 current, Vector3 tangentU, Vector3 tangentV)
        {
            Vector3 delta = current - start;
            Vector3 u = tangentU * Vector3.Dot(delta, tangentU);
            Vector3 v = tangentV * Vector3.Dot(delta, tangentV);
            return new[]
            {
                start, start + u, start + u + v, start + v
            };
        }

        private void CancelAnnotationDrag()
        {
            _annotationDrag = default;
            if (_annotationDraftObject != null) Destroy(_annotationDraftObject);
            if (_annotationDraftMesh != null) Destroy(_annotationDraftMesh);
            _annotationDraftObject = null;
            _annotationDraftLine = null;
            _annotationDraftMesh = null;
        }

        private void LoadAnnotations()
        {
            _annotations.Clear();
            _nextAnnotationId = 1;
            _selectedAnnotationId = 0;
            _annotationsDirty = false;
            _annotationsLoaded = false;
            if (string.IsNullOrWhiteSpace(_annotationPath))
            {
                RefreshAnnotationObjects();
                return;
            }
            if (!File.Exists(_annotationPath))
            {
                _annotationsLoaded = true;
                RefreshAnnotationObjects();
                return;
            }
            try
            {
                AnnotationFile file = JsonUtility.FromJson<AnnotationFile>(
                    File.ReadAllText(_annotationPath));
                if (file?.format != "QuestMerkabaAnnotations" ||
                    (file.version != 1 && file.version != 2) ||
                    file.items == null)
                    throw new InvalidDataException("Unsupported annotation document.");
                _annotations.AddRange(file.items);
                _nextAnnotationId = Mathf.Max(file.nextId, 1);
                _annotationsLoaded = true;
                bool migrated = false;
                bool canMigratePaint = _paintEngine != null &&
                    _paintEngine.IsOpen && _packageSpatialBinding.HasValue &&
                    _scanner != null && _scanner.ActiveAnchorUuid != Guid.Empty &&
                    _packageSpatialBinding.Value.AnchorUuid ==
                    _scanner.ActiveAnchorUuid &&
                    RoomSpaceRoot.RoomSpaceReady;
                if (canMigratePaint)
                {
                    for (int index = _annotations.Count - 1;
                         index >= 0; index--)
                    {
                        AnnotationRecord annotation = _annotations[index];
                        if (!IsPaintAnnotation(annotation) ||
                            annotation.points == null ||
                            annotation.points.Length == 0) continue;
                        MerkabaDesignTool tool = annotation.type switch
                        {
                            "paint-line" => MerkabaDesignTool.Line,
                            "paint-surface" =>
                                MerkabaDesignTool.SurfaceBrush,
                            _ => MerkabaDesignTool.SpatialBrush
                        };
                        Vector3[] points = Array.ConvertAll(annotation.points,
                            AnnotationWorldPoint);
                        if (!_paintEngine.ImportLegacy(tool,
                                annotation.styled ? annotation.color :
                                    Color.white,
                                annotation.width > 0f ? annotation.width :
                                    0.01f, points)) continue;
                        _annotations.RemoveAt(index);
                        migrated = true;
                    }
                }
                if (migrated)
                {
                    MarkAnnotationsDirty();
                    SaveDesign();
                }
            }
            catch (Exception exception)
            {
                Logger.Warning("Could not load GLB View annotations: " +
                    exception.Message);
            }
            RefreshAnnotationObjects();
        }

        // Native packages preserve this existing document schema, not a new
        // annotation representation. Absent source notes are explicitly empty.
        internal static void ValidateNativeAnnotations(string path, bool createEmpty = false)
        {
            if (!File.Exists(path))
            {
                if (!createEmpty) throw new FileNotFoundException("Native annotations are missing.", path);
                var empty = new AnnotationFile
                {
                    format = "QuestMerkabaAnnotations", version = 2,
                    nextId = 1, items = Array.Empty<AnnotationRecord>()
                };
                File.WriteAllText(path, JsonUtility.ToJson(empty, false), new UTF8Encoding(false));
            }
            AnnotationFile file = JsonUtility.FromJson<AnnotationFile>(File.ReadAllText(path, Encoding.UTF8));
            if (file?.format != "QuestMerkabaAnnotations" || (file.version != 1 && file.version != 2) ||
                file.items == null || file.nextId <= 0)
                throw new InvalidDataException("Unsupported native annotation snapshot.");
            var ids = new HashSet<int>();
            foreach (AnnotationRecord item in file.items)
            {
                if (item == null || item.id <= 0 || !ids.Add(item.id) || item.id >= file.nextId ||
                    item.points == null || !float.IsFinite(item.width) ||
                    !float.IsFinite(item.color.r) || !float.IsFinite(item.color.g) ||
                    !float.IsFinite(item.color.b) || !float.IsFinite(item.color.a))
                    throw new InvalidDataException("Invalid native annotation identity/style.");
                foreach (Vector3 point in item.points)
                    if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                        throw new InvalidDataException("Nonfinite native annotation coordinate.");
            }
        }

        private void RefreshResidency()
        {
            if (!IsOpen || _tiles.Count == 0) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            Vector3 pointerOrigin = default;
            Vector3 pointerDirection = default;
            bool hasPointer = _rayDriver != null &&
                _rayDriver.TryGetWorldRay(out pointerOrigin,
                    out pointerDirection);
            var pointerRay = new Ray(pointerOrigin, pointerDirection);
            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
            long budget = ResidentDecodedBudgetBytes();
            bool loadWholePackage = FitsResidentBudget(
                _totalResidentEstimateBytes, budget);
            _desiredTiles.Clear();
            foreach (Tile tile in _tiles)
            {
                Bounds worldBounds = TransformBounds(_modelRoot,
                    Centered(tile.Bounds));
                bool inView = GeometryUtility.TestPlanesAABB(_frustumPlanes,
                    worldBounds);
                float score;
                if (inView && hasPointer && worldBounds.IntersectRay(pointerRay,
                        out float pointerDistance))
                    score = -1000000f + pointerDistance;
                else
                    score = Vector3.SqrMagnitude(worldBounds.center -
                        camera.transform.position) +
                        (inView ? 0f : 100000000f);
                _desiredTiles.Add((tile, score));
            }
            _desiredTiles.Sort((left, right) =>
            {
                int score = left.Score.CompareTo(right.Score);
                return score != 0 ? score : string.CompareOrdinal(
                    left.Tile.Uri, right.Tile.Uri);
            });
            _keptTiles.Clear();
            long used = 0L;
            foreach ((Tile tile, _) in _desiredTiles)
            {
                long bytes = tile.ResidentBytes > 0L
                    ? tile.ResidentBytes : tile.EstimatedResidentBytes;
                if (bytes > budget - used)
                    continue;
                _keptTiles.Add(tile);
                used = checked(used + bytes);
            }
            foreach (Tile tile in _tiles)
                if (tile.Object != null && !_keptTiles.Contains(tile))
                    DestroyTile(tile);
            StartPendingTileLoads();
            UpdateContinuationMarker(camera);
            Status = $"GLB preview-only · {LoadedTileCount}/{_tiles.Count} tiles · " +
                $"{FormatBytes(ResidentDecodedBytes())} resident / " +
                $"{FormatBytes(_totalModelBytes)} model · " +
                (loadWholePackage ? "full-load" : "spatial streaming");
        }

        private void StartPendingTileLoads()
        {
            foreach ((Tile tile, _) in _desiredTiles)
            {
                if (_tileLoadsInFlight >= MaximumConcurrentTileLoads)
                    return;
                if (!_keptTiles.Contains(tile) || tile.Object != null ||
                    tile.Loading || tile.Failed)
                    continue;
                _ = LoadTileAsync(tile, _generation);
            }
        }

        private static long ResidentDecodedBudgetBytes()
        {
            long systemBytes = Math.Max(0L, (long)SystemInfo.systemMemorySize) *
                1024L * 1024L;
            if (systemBytes <= 0L) return LargePackageBytes;
            long adaptive = systemBytes * 3L / 8L;
            return Math.Max(LargePackageBytes, adaptive);
        }

        internal static bool FitsResidentBudget(long packageBytes,
            long residentBudgetBytes) => packageBytes >= 0L &&
            packageBytes <= residentBudgetBytes;

        internal static bool NeedsContinuationMarker(long packageBytes,
            int loadedTileCount, int tileCount) =>
            packageBytes > LargePackageBytes && tileCount > 0 &&
            loadedTileCount < tileCount;

        private void UpdateContinuationMarker(Camera camera)
        {
            if (!NeedsContinuationMarker(_totalModelBytes,
                    LoadedTileCount, _tiles.Count))
            {
                if (_continuationMarker != null)
                    _continuationMarker.SetActive(false);
                return;
            }

            Tile nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (Tile tile in _tiles)
            {
                if (tile.Object != null) continue;
                Bounds bounds = TransformBounds(_modelRoot,
                    Centered(tile.Bounds));
                float distance = Vector3.SqrMagnitude(bounds.center -
                    camera.transform.position);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = tile;
            }
            if (nearest == null) return;

            EnsureContinuationMarker();
            _continuationMarker.SetActive(true);
            Vector3 localTarget = Centered(nearest.Bounds).center;
            _continuationMarker.transform.localPosition = localTarget;
            Vector3 worldDirection = _modelRoot.TransformPoint(localTarget) -
                _modelRoot.TransformPoint(Vector3.zero);
            Vector3 towardCamera = camera.transform.position -
                _continuationMarker.transform.position;
            if (towardCamera.sqrMagnitude < 1e-6f)
                towardCamera = -camera.transform.forward;
            Quaternion facing = Quaternion.LookRotation(
                towardCamera.normalized, camera.transform.up);
            float angle = Mathf.Atan2(Vector3.Dot(worldDirection,
                    camera.transform.up), Vector3.Dot(worldDirection,
                    camera.transform.right)) * Mathf.Rad2Deg;
            _continuationMarker.transform.rotation = facing *
                Quaternion.AngleAxis(angle, Vector3.forward);
            float inverseScale = 1f / Mathf.Max(_modelRoot.lossyScale.x, 1e-5f);
            _continuationMarker.transform.localScale = Vector3.one *
                (0.16f * inverseScale);
        }

        private void EnsureContinuationMarker()
        {
            if (_continuationMarker != null) return;
            _continuationMarker = new GameObject("Model Continues");
            _continuationMarker.transform.SetParent(_modelRoot, false);
            _continuationMesh = new Mesh
            {
                name = "Merkaba Model Continuation",
                vertices = new[]
                {
                    new Vector3(-0.62f, -0.48f, 0f),
                    new Vector3(0.62f, -0.48f, 0f),
                    new Vector3(0.62f, 0.48f, 0f),
                    new Vector3(-0.62f, 0.48f, 0f),
                    new Vector3(-0.48f, -0.12f, -0.002f),
                    new Vector3(0.05f, -0.12f, -0.002f),
                    new Vector3(0.05f, 0.12f, -0.002f),
                    new Vector3(-0.48f, 0.12f, -0.002f),
                    new Vector3(0.05f, -0.34f, -0.002f),
                    new Vector3(0.55f, 0f, -0.002f),
                    new Vector3(0.05f, 0.34f, -0.002f)
                },
                triangles = new[]
                {
                    0, 1, 2, 0, 2, 3,
                    4, 5, 6, 4, 6, 7,
                    8, 9, 10
                },
                colors32 = new[]
                {
                    new Color32(35, 155, 220, 45),
                    new Color32(35, 155, 220, 45),
                    new Color32(35, 155, 220, 45),
                    new Color32(35, 155, 220, 45),
                    new Color32(70, 220, 255, 235),
                    new Color32(70, 220, 255, 235),
                    new Color32(70, 220, 255, 235),
                    new Color32(70, 220, 255, 235),
                    new Color32(70, 220, 255, 235),
                    new Color32(70, 220, 255, 235),
                    new Color32(70, 220, 255, 235)
                }
            };
            _continuationMesh.RecalculateNormals();
            _continuationMesh.RecalculateBounds();
            var filter = _continuationMarker.AddComponent<MeshFilter>();
            filter.sharedMesh = _continuationMesh;
            _continuationMaterial = new Material(previewShader)
            {
                name = "Merkaba Model Continuation",
                hideFlags = HideFlags.DontSave
            };
            ConfigureMaterial(_continuationMaterial, Color.white, false);
            var renderer = _continuationMarker.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _continuationMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private long ResidentDecodedBytes()
        {
            long bytes = 0L;
            foreach (Tile tile in _tiles)
                if (tile.Object != null) bytes = checked(bytes +
                    tile.ResidentBytes);
            return bytes;
        }

        private static string FormatBytes(long bytes) => bytes >= 1024L *
            1024L * 1024L
            ? $"{bytes / (1024d * 1024d * 1024d):F1} GiB"
            : $"{bytes / (1024d * 1024d):F0} MiB";

        internal static long EstimateResidentBytes(long decodedBytes) =>
            decodedBytes <= 0L ? 0L : checked(decodedBytes * 2L);

        private Bounds Centered(Bounds source) =>
            new(source.center - _scanCenter, source.size);

        private async Task LoadTileAsync(Tile tile, int generation)
        {
            if (tile.Loading || tile.Object != null ||
                _tileLoadsInFlight >= MaximumConcurrentTileLoads) return;
            tile.Loading = true;
            _tileLoadsInFlight++;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            string archivePath = _archivePath;
            long decodedLimit = ResidentDecodedBudgetBytes() / 2L;
            try
            {
                using ParsedGlb parsed = await Task.Run(() => ReadGlbTile(
                    archivePath, tile, decodedLimit));
                if (generation != _generation || !IsOpen) return;
                if (!_keptTiles.Contains(tile)) return;
                // Keep the same cache policy, but learn the expanded PNG/UV
                // cost before publication instead of retaining ZIP-size cost.
                long actualResident = EstimateResidentBytes(parsed.DecodedBytes);
                _totalResidentEstimateBytes = checked(_totalResidentEstimateBytes +
                    actualResident - tile.EstimatedResidentBytes);
                tile.EstimatedResidentBytes = actualResident;
                // PNG compression ratio is not a residency budget. A load
                // finishing after another tile must still fit actual decoded
                // CPU/GPU texture+mesh cost before any Unity object exists.
                if (actualResident > ResidentDecodedBudgetBytes() - ResidentDecodedBytes())
                {
                    _keptTiles.Remove(tile);
                    return;
                }
                double parseMilliseconds = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                CreateTileObject(tile, parsed);
                Logger.Info($"Merkaba GLB View loaded {tile.Uri}: " +
                    $"parse={parseMilliseconds:F1} ms, " +
                    $"mesh={timer.Elapsed.TotalMilliseconds:F1} ms, " +
                    $"vertices={parsed.Positions.Length}, " +
                    $"triangles={parsed.Indices.Length / 3}.");
            }
            catch (Exception exception)
            {
                if (generation != _generation || !IsOpen) return;
                tile.Failed = true;
                Logger.Warning($"Could not preview {tile.Uri}: " +
                    exception.Message);
                Status = $"GLB View tile failed: {tile.Uri}";
            }
            finally
            {
                tile.Loading = false;
                if (generation == _generation)
                {
                    _tileLoadsInFlight = Mathf.Max(0,
                        _tileLoadsInFlight - 1);
                    if (IsOpen) StartPendingTileLoads();
                }
            }
        }

        private void CreateTileObject(Tile tile, ParsedGlb parsed)
        {
            PreviewGlb presentation = PreviewGlb.Create(parsed, _modelMaterial, "Merkaba " + tile.Uri);
            Mesh mesh = presentation.Mesh;

            var tileObject = new GameObject(tile.Uri.Replace('/', '_'));
            tileObject.transform.SetParent(_modelRoot, false);
            tileObject.transform.localPosition = tile.OriginUnity - _scanCenter;
            var filter = tileObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = tileObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = presentation.Materials;
            renderer.enabled = _previewOpacity > 0.001f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            tile.Object = tileObject;
            tile.Mesh = mesh;
            tile.Presentation = presentation;
            tile.ResidentBytes = EstimateResidentBytes(parsed.DecodedBytes);
            if (TileCollidersRequired()) EnsureTileCollider(tile);
        }

        private void RefreshTileColliders()
        {
            bool required = TileCollidersRequired();
            foreach (Tile tile in _tiles)
            {
                if (tile.Object == null) continue;
                if (required)
                {
                    EnsureTileCollider(tile);
                    continue;
                }
                if (tile.Collider != null)
                {
                    Destroy(tile.Collider);
                    tile.Collider = null;
                }
            }
        }

        private bool TileCollidersRequired() =>
            (_objectInputEnabled && _objectSurfaceSnap &&
                (_designLibrary?.PlacementEnabled ?? false)) ||
            (_paintInputEnabled && (_paintTool == MerkabaArtifactPaintTool.Eyedropper ||
                _paintTool ==
                MerkabaArtifactPaintTool.Line || _paintTool ==
                MerkabaArtifactPaintTool.Brush || _paintTool ==
                MerkabaArtifactPaintTool.SurfaceBrush)) ||
            _annotationMode == AnnotationMode.Point ||
            _annotationMode == AnnotationMode.Line ||
            _annotationMode == AnnotationMode.Plane ||
            _annotationMode == AnnotationMode.Move ||
            _annotationMode == AnnotationMode.Select;

        private static void DestroyTile(Tile tile)
        {
            if (tile.Object != null) Destroy(tile.Object);
            tile.Presentation?.Dispose();
            tile.Object = null;
            tile.Mesh = null;
            tile.Presentation = null;
            tile.Collider = null;
            tile.ResidentBytes = 0L;
        }

        private static Bounds TransformBounds(Transform transform,
            Bounds local)
        {
            Vector3 center = transform.TransformPoint(local.center);
            Vector3 extents = local.extents;
            Vector3 axisX = transform.TransformVector(extents.x, 0f, 0f);
            Vector3 axisY = transform.TransformVector(0f, extents.y, 0f);
            Vector3 axisZ = transform.TransformVector(0f, 0f, extents.z);
            Vector3 worldExtents = new(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, worldExtents * 2f);
        }

        private static PackageIndex ReadPackageIndex(string archivePath)
        {
            using var stream = new FileStream(archivePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            ZipArchiveEntry manifest = FindTilesetManifest(archive);
            string manifestName = manifest.FullName.Replace('\\', '/');
            int slash = manifestName.LastIndexOf('/');
            string packagePrefix = slash >= 0
                ? manifestName.Substring(0, slash + 1) : string.Empty;
            string json;
            using (Stream input = manifest.Open())
            using (var reader = new StreamReader(input, Encoding.UTF8, true,
                       64 * 1024, false))
                json = reader.ReadToEnd();
            TilesetDocument document = JsonUtility.FromJson<TilesetDocument>(json);
            if (document?.root == null)
                throw new InvalidDataException("Invalid 3D Tiles root.");
            MerkabaSpatialBinding? spatialBinding =
                TryParseSpatialBinding(json, out MerkabaSpatialBinding parsed)
                    ? parsed : null;
            var tiles = new List<Tile>();
            CollectTiles(document.root, Matrix4x4.identity, archive,
                packagePrefix, tiles);
            if (tiles.Count == 0)
                throw new InvalidDataException("3D Tiles export has no GLB leaves.");
            tiles.Sort((left, right) => string.CompareOrdinal(left.Uri,
                right.Uri));
            Bounds bounds = ConvertTilesetBox(document.root.boundingVolume?.box,
                ReadColumnMajorMatrix(document.root.transform));
            long totalModelBytes = 0L;
            long totalResidentEstimateBytes = 0L;
            foreach (Tile tile in tiles)
            {
                totalModelBytes = checked(totalModelBytes + tile.ArchiveBytes);
                totalResidentEstimateBytes = checked(
                    totalResidentEstimateBytes + tile.EstimatedResidentBytes);
            }
            return new PackageIndex(tiles, bounds, totalModelBytes,
                totalResidentEstimateBytes, spatialBinding);
        }

        private static ZipArchiveEntry FindTilesetManifest(ZipArchive archive)
        {
            ZipArchiveEntry exact = archive.GetEntry("tileset.json");
            if (exact != null) return exact;
            ZipArchiveEntry match = null;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (!name.EndsWith("/tileset.json",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (match != null)
                    throw new InvalidDataException(
                        "ZIP contains more than one tileset.json.");
                match = entry;
            }
            return match ?? throw new InvalidDataException(
                "tileset.json is missing.");
        }

        private static void CollectTiles(TilesetNode node, Matrix4x4 parent,
            ZipArchive archive, string packagePrefix, List<Tile> output)
        {
            Matrix4x4 local = ReadColumnMajorMatrix(node.transform);
            Matrix4x4 current = parent * local;
            if (node.content != null && !string.IsNullOrWhiteSpace(
                    node.content.uri))
            {
                string uri = ResolveArchiveUri(packagePrefix,
                    node.content.uri);
                ZipArchiveEntry entry = archive.GetEntry(uri) ??
                    throw new InvalidDataException("Missing tile " + uri);
                output.Add(new Tile(uri, entry.Length,
                    TilesetToUnity(current.MultiplyPoint3x4(Vector3.zero)),
                    ConvertTilesetBox(node.boundingVolume?.box, current)));
            }
            if (node.children == null) return;
            foreach (TilesetNode child in node.children)
                if (child != null) CollectTiles(child, current, archive,
                    packagePrefix, output);
        }

        private static string ResolveArchiveUri(string packagePrefix,
            string contentUri)
        {
            string uri = contentUri.Replace('\\', '/').TrimStart('/');
            if (uri.Contains("://", StringComparison.Ordinal) ||
                uri == ".." || uri.StartsWith("../", StringComparison.Ordinal) ||
                uri.Contains("/../", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "3D Tiles content URI must stay inside its ZIP.");
            return packagePrefix + uri;
        }

        internal static bool TryParseSpatialBinding(string json,
            out MerkabaSpatialBinding binding)
        {
            binding = default;
            if (string.IsNullOrWhiteSpace(json)) return false;
            TilesetDocument document = JsonUtility.FromJson<TilesetDocument>(
                json);
            TilesetSpatialBinding encoded = document?.asset?.extras?
                .questMerkabaSpatialBinding;
            if (encoded == null || encoded.version !=
                MerkabaSpatialBinding.CurrentVersion ||
                !Guid.TryParse(encoded.anchorUuid, out Guid uuid) ||
                encoded.anchorFromPackage == null ||
                encoded.anchorFromPackage.Length != 16)
                return false;
            binding = new MerkabaSpatialBinding(uuid,
                ReadColumnMajorMatrix(encoded.anchorFromPackage));
            return binding.IsValid;
        }

        private static Matrix4x4 ReadColumnMajorMatrix(float[] values)
        {
            if (values == null || values.Length != 16)
                return Matrix4x4.identity;
            var matrix = new Matrix4x4();
            for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++)
                matrix[row, column] = values[column * 4 + row];
            return matrix;
        }

        private static Bounds ConvertTilesetBox(float[] box,
            Matrix4x4 transform)
        {
            if (box == null || box.Length != 12)
                throw new InvalidDataException("Invalid 3D Tiles box.");
            Vector3 center = new(box[0], box[1], box[2]);
            Vector3 x = new(box[3], box[4], box[5]);
            Vector3 y = new(box[6], box[7], box[8]);
            Vector3 z = new(box[9], box[10], box[11]);
            bool first = true;
            Bounds result = default;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iy = -1; iy <= 1; iy += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                Vector3 point = transform.MultiplyPoint3x4(center + x * ix +
                    y * iy + z * iz);
                Vector3 unity = TilesetToUnity(point);
                if (first)
                {
                    result = new Bounds(unity, Vector3.zero);
                    first = false;
                }
                else result.Encapsulate(unity);
            }
            return result;
        }

        private static Vector3 TilesetToUnity(Vector3 value) =>
            new(-value.x, value.z, -value.y);

        private static ParsedGlb ReadGlbTile(string archivePath, Tile tile,
            long maximumDecodedBytes)
        {
            using var stream = new FileStream(archivePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.RandomAccess);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            ZipArchiveEntry entry = archive.GetEntry(tile.Uri) ??
                throw new InvalidDataException("Missing tile " + tile.Uri);
            using Stream entryStream = entry.Open();
            using var input = new BufferedStream(entryStream, 1024 * 1024);
            return ParseGlbForPreview(input, entry.Length, maximumDecodedBytes);
        }

        internal static ParsedGlb ParseGlbForPreview(byte[] bytes,
            long maximumDecodedBytes = LargePackageBytes / 2L)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var input = new MemoryStream(bytes, false);
            return ParseGlbForPreview(input, bytes.LongLength, maximumDecodedBytes);
        }

        internal static ParsedGlb ParseGlbForPreview(Stream input,
            long streamLength, long maximumDecodedBytes = LargePackageBytes / 2L)
        {
            if (maximumDecodedBytes <= 0L)
                throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
            if (input == null || !input.CanRead)
                throw new ArgumentException("GLB input must be readable.",
                    nameof(input));
            if (streamLength < 28L)
                throw new InvalidDataException("Invalid GLB header.");
            byte[] scratch = new byte[GlbReadBufferBytes];
            ReadExactly(input, scratch, 0, 12);
            if (ReadUInt32(scratch, 0) != GlbMagic ||
                ReadUInt32(scratch, 4) != 2u ||
                ReadUInt32(scratch, 8) != streamLength)
                throw new InvalidDataException("Invalid GLB header.");

            ReadExactly(input, scratch, 0, 8);
            uint jsonLengthValue = ReadUInt32(scratch, 0);
            if (jsonLengthValue > int.MaxValue ||
                ReadUInt32(scratch, 4) != JsonChunkType ||
                jsonLengthValue < 2u || 20L + jsonLengthValue + 8L >
                streamLength)
                throw new InvalidDataException("Invalid GLB JSON chunk.");
            int jsonLength = (int)jsonLengthValue;
            byte[] jsonBytes = new byte[jsonLength];
            ReadExactly(input, jsonBytes, 0, jsonBytes.Length);
            string json = Encoding.UTF8.GetString(jsonBytes).TrimEnd(
                '\0', ' ', '\t', '\r', '\n');

            ReadExactly(input, scratch, 0, 8);
            uint binaryLengthValue = ReadUInt32(scratch, 0);
            if (binaryLengthValue > int.MaxValue ||
                ReadUInt32(scratch, 4) != BinaryChunkType ||
                28L + jsonLength + binaryLengthValue != streamLength)
                throw new InvalidDataException("Invalid GLB binary chunk.");
            int binaryLength = (int)binaryLengthValue;
            GlbDocument document = JsonUtility.FromJson<GlbDocument>(MarkCapturedTexturePresence(json));
            if (document?.bufferViews == null ||
                document.accessors == null || document.meshes == null || document.meshes.Length != 1 ||
                document.meshes[0]?.primitives == null || document.meshes[0].primitives.Length == 0)
                throw new InvalidDataException("Unsupported GLB layout.");
            GlbPrimitive[] primitives = document.meshes[0].primitives;
            GlbAttributes attributes = primitives[0]?.attributes ??
                throw new InvalidDataException("GLB primitive has no vertex attributes.");
            GlbAccessor position = ReadAccessor(document, attributes.POSITION);
            GlbAccessor normal = ReadAccessor(document, attributes.NORMAL);
            GlbAccessor color = ReadAccessor(document, attributes.COLOR_0);
            if (position.componentType != 5126 || position.type != "VEC3" ||
                normal.componentType != 5126 || normal.type != "VEC3" ||
                color.componentType != 5121 || color.type != "VEC4" ||
                !color.normalized || position.count <= 0 || position.count != normal.count ||
                position.count != color.count || position.byteOffset != 0 || normal.byteOffset != 0 || color.byteOffset != 0)
                throw new InvalidDataException("Unsupported GLB accessor ABI.");
            GlbBufferView positionView = ReadView(document, position.bufferView);
            GlbBufferView normalView = ReadView(document, normal.bufferView);
            GlbBufferView colorView = ReadView(document, color.bufferView);
            GlbBufferView indexView = null;
            ParsedMaterial[] materials = ReadMaterials(document);
            var primitiveRanges = new ParsedPrimitive[primitives.Length];
            int indexViewId = -1, indexCount = 0, primitiveOrdinal = 0, uvAccessorId = -1;
            foreach (GlbPrimitive primitive in primitives)
            {
                if (primitive?.attributes == null || primitive.mode != 4 ||
                    primitive.attributes.POSITION != attributes.POSITION ||
                    primitive.attributes.NORMAL != attributes.NORMAL || primitive.attributes.COLOR_0 != attributes.COLOR_0)
                    throw new InvalidDataException("GLB primitives must share the current Flower vertex streams.");
                GlbAccessor index = ReadAccessor(document, primitive.indices);
                if (index.componentType != 5125 || index.type != "SCALAR" || index.count <= 0 ||
                    index.count % 3 != 0 || index.normalized || index.byteOffset != (long)indexCount * 4L ||
                    (indexViewId >= 0 && index.bufferView != indexViewId))
                    throw new InvalidDataException("GLB primitive indices are not a contiguous triangle partition.");
                indexViewId = index.bufferView;
                indexView = ReadView(document, indexViewId);
                if ((uint)primitive.material >= materials.Length)
                    throw new InvalidDataException("Invalid GLB primitive material.");
                if (materials[primitive.material].Image >= 0)
                {
                    int uv = primitive.attributes.TEXCOORD_0;
                    if (uv < 0) throw new InvalidDataException("Captured texture has no UV stream.");
                    if (uvAccessorId >= 0 && uvAccessorId != uv)
                        throw new InvalidDataException("Flower primitives must share one UV stream.");
                    uvAccessorId = uv;
                }
                primitiveRanges[primitiveOrdinal++] = new ParsedPrimitive(indexCount, index.count, primitive.material);
                indexCount = checked(indexCount + index.count);
            }
            ValidateView(positionView, position.count, 12, binaryLength);
            ValidateView(normalView, normal.count, 12, binaryLength);
            ValidateView(colorView, color.count, 4, binaryLength);
            ValidateView(indexView, indexCount, 4, binaryLength);
            GlbBufferView uvView = null;
            if (uvAccessorId >= 0)
            {
                GlbAccessor uv = ReadAccessor(document, uvAccessorId);
                if (uv.componentType != 5126 || uv.type != "VEC2" || uv.normalized ||
                    uv.byteOffset != 0 || uv.count != position.count)
                    throw new InvalidDataException("Invalid Flower UV accessor.");
                uvView = ReadView(document, uv.bufferView);
                ValidateView(uvView, uv.count, 8, binaryLength);
            }
            var requiredImages = new bool[document.images?.Length ?? 0];
            long decodedBytes = checked((long)position.count * (28L + (uvView == null ? 0L : 8L)) +
                4L * indexCount);
            foreach (ParsedMaterial material in materials)
                if (material.Image >= 0 && !requiredImages[material.Image])
                {
                    requiredImages[material.Image] = true;
                    decodedBytes = checked(decodedBytes + CapturedImage.DecodedByteLength);
                }
            if (decodedBytes > maximumDecodedBytes)
                throw new InvalidDataException($"GLB preview needs {decodedBytes} decoded bytes, " +
                    $"above the {maximumDecodedBytes}-byte resident content budget.");
            var positions = new Vector3[position.count];
            var normals = new Vector3[position.count];
            var colors = new Color32[position.count];
            var indices = new int[indexCount];
            long binaryCursor = 0L;
            MoveToView(input, positionView, ref binaryCursor,
                scratch);
            for (int vertexBase = 0; vertexBase < position.count;)
            {
                int batch = Math.Min(position.count - vertexBase,
                    scratch.Length / 12);
                ReadExactly(input, scratch, 0, batch * 12);
                for (int local = 0; local < batch; local++)
                {
                    int offset = local * 12;
                    Vector3 glb = new(ReadSingle(scratch, offset),
                        ReadSingle(scratch, offset + 4),
                        ReadSingle(scratch, offset + 8));
                    positions[vertexBase + local] =
                        new Vector3(-glb.x, glb.y, glb.z);
                }
                vertexBase += batch;
            }
            binaryCursor += positionView.byteLength;
            MoveToView(input, normalView, ref binaryCursor,
                scratch);
            for (int vertexBase = 0; vertexBase < normal.count;)
            {
                int batch = Math.Min(normal.count - vertexBase,
                    scratch.Length / 12);
                ReadExactly(input, scratch, 0, batch * 12);
                for (int local = 0; local < batch; local++)
                {
                    int offset = local * 12;
                    Vector3 glbNormal = new(ReadSingle(scratch, offset),
                        ReadSingle(scratch, offset + 4),
                        ReadSingle(scratch, offset + 8));
                    normals[vertexBase + local] = new Vector3(-glbNormal.x,
                        glbNormal.y, glbNormal.z).normalized;
                }
                vertexBase += batch;
            }
            binaryCursor += normalView.byteLength;
            MoveToView(input, colorView, ref binaryCursor,
                scratch);
            for (int vertexBase = 0; vertexBase < color.count;)
            {
                int batch = Math.Min(color.count - vertexBase,
                    scratch.Length / 4);
                ReadExactly(input, scratch, 0, batch * 4);
                for (int local = 0; local < batch; local++)
                {
                    int offset = local * 4;
                    colors[vertexBase + local] = new Color32(scratch[offset],
                        scratch[offset + 1], scratch[offset + 2],
                        scratch[offset + 3]);
                }
                vertexBase += batch;
            }
            binaryCursor += colorView.byteLength;
            MoveToView(input, indexView, ref binaryCursor,
                scratch);
            for (int valueBase = 0; valueBase < indexCount;)
            {
                int batch = Math.Min(indexCount - valueBase,
                    scratch.Length / 4);
                ReadExactly(input, scratch, 0, batch * 4);
                for (int local = 0; local < batch; local++)
                {
                    uint read = ReadUInt32(scratch, local * 4);
                    if (read >= position.count)
                        throw new InvalidDataException(
                            "GLB index out of range.");
                    indices[valueBase + local] = (int)read;
                }
                valueBase += batch;
            }
            binaryCursor += indexView.byteLength;
            Vector2[] uvs = uvView == null ? Array.Empty<Vector2>() : new Vector2[position.count];
            if (uvView != null)
            {
                MoveToView(input, uvView, ref binaryCursor, scratch);
                for (int vertexBase = 0; vertexBase < uvs.Length;)
                {
                    int batch = Math.Min(uvs.Length - vertexBase, scratch.Length / 8);
                    ReadExactly(input, scratch, 0, batch * 8);
                    for (int local = 0; local < batch; local++)
                    {
                        float u = ReadSingle(scratch, 8 * local), v = ReadSingle(scratch, 8 * local + 4);
                        if (!float.IsFinite(u) || !float.IsFinite(v))
                            throw new InvalidDataException("Non-finite Flower UV.");
                        // glTF image origin is upper-left; Unity's texture
                        // sampling origin is lower-left. Geometry is untouched.
                        uvs[vertexBase + local] = new Vector2(u, 1f - v);
                    }
                    vertexBase += batch;
                }
                binaryCursor += uvView.byteLength;
            }
            CapturedImage[] images = ReadCapturedImages(input, document, requiredImages,
                binaryLength, ref binaryCursor, scratch);
            try
            {
                SkipExactly(input, binaryLength - binaryCursor, scratch);
                for (int triangle = 0; triangle < indices.Length; triangle += 3)
                    (indices[triangle + 1], indices[triangle + 2]) =
                        (indices[triangle + 2], indices[triangle + 1]);
                return new ParsedGlb(positions, normals, colors, indices, uvs, primitiveRanges, materials, images);
            }
            catch
            {
                foreach (CapturedImage image in images) image?.Dispose();
                throw;
            }
        }

        // JsonUtility can construct an absent nested class. Mark only real
        // property tokens before decoding; this is not a second JSON parser.
        // The writer emits unescaped property names. Escaped string VALUES
        // are skipped intact, and cannot introduce a presence marker.
        private static string MarkCapturedTexturePresence(string json)
        {
            StringBuilder marked = null;
            int copied = 0;
            for (int cursor = 0; cursor < json.Length;)
            {
                if (json[cursor] != '"') { cursor++; continue; }
                int token = cursor++, first = cursor;
                bool escaped = false;
                while (cursor < json.Length && json[cursor] != '"')
                {
                    if (json[cursor] == '\\') { escaped = true; cursor += 2; }
                    else cursor++;
                }
                if (cursor >= json.Length) throw new InvalidDataException("Unterminated GLB JSON string.");
                int end = cursor++;
                int colon = cursor;
                while (colon < json.Length && char.IsWhiteSpace(json[colon])) colon++;
                if (colon == json.Length || json[colon] != ':') continue;
                if (escaped) throw new InvalidDataException("Unsupported escaped Flower property name.");
                ReadOnlySpan<char> key = json.AsSpan(first, end - first);
                if (key.StartsWith("_m8Preview", StringComparison.Ordinal))
                    throw new InvalidDataException("Reserved Flower preview property.");
                string marker;
                int value = colon + 1;
                while (value < json.Length && char.IsWhiteSpace(json[value])) value++;
                if (key.SequenceEqual("baseColorTexture"))
                {
                    if (value == json.Length || json[value] != '{')
                        throw new InvalidDataException("Captured texture reference must be an object.");
                    marker = "_m8PreviewHasBaseColorTexture";
                }
                else if (key.SequenceEqual("index"))
                {
                    int last = value;
                    while (last < json.Length && json[last] >= '0' && json[last] <= '9') last++;
                    int separator = last;
                    while (separator < json.Length && char.IsWhiteSpace(json[separator])) separator++;
                    if (last == value || (last - value > 1 && json[value] == '0') || separator == json.Length ||
                        (json[separator] != ',' && json[separator] != '}') ||
                        !int.TryParse(json.AsSpan(value, last - value), out _))
                        throw new InvalidDataException("Captured texture index must be a nonnegative Int32.");
                    marker = "_m8PreviewHasIndex";
                }
                else if (key.SequenceEqual("uri")) marker = "_m8PreviewHasUri";
                else continue;
                marked ??= new StringBuilder(json.Length);
                marked.Append(json, copied, token - copied);
                marked.Append('"').Append(marker).Append("\":true,");
                copied = token;
            }
            return marked == null ? json : marked.Append(json, copied, json.Length - copied).ToString();
        }

        private static ParsedMaterial[] ReadMaterials(GlbDocument document)
        {
            if (document.materials == null || document.materials.Length == 0)
                throw new InvalidDataException("Flower GLB has no presentation materials.");
            var result = new ParsedMaterial[document.materials.Length];
            for (int index = 0; index < result.Length; index++)
            {
                GlbPbr pbr = document.materials[index]?.pbrMetallicRoughness ??
                    throw new InvalidDataException("Invalid Flower material.");
                float[] values = pbr.baseColorFactor;
                if (values == null || values.Length != 4)
                    throw new InvalidDataException("Invalid captured color factor.");
                foreach (float value in values)
                    if (!float.IsFinite(value) || value < 0f || value > 1f)
                        throw new InvalidDataException("Invalid captured color factor.");
                int image = -1;
                if (pbr._m8PreviewHasBaseColorTexture)
                {
                    if (pbr.baseColorTexture == null || !pbr.baseColorTexture._m8PreviewHasIndex)
                        throw new InvalidDataException("Captured texture reference has no index.");
                    int texture = pbr.baseColorTexture.index;
                    if (pbr.baseColorTexture.texCoord != 0 || document.textures == null ||
                        (uint)texture >= document.textures.Length || document.textures[texture] == null ||
                        document.textures[texture].sampler != 0)
                        throw new InvalidDataException("Unsupported captured texture reference.");
                    image = document.textures[texture].source;
                    if (document.images == null || (uint)image >= document.images.Length)
                        throw new InvalidDataException("Captured texture has no embedded image.");
                }
                result[index] = new ParsedMaterial(new Color(values[0], values[1], values[2], values[3]), image);
            }
            return result;
        }

        private static CapturedImage[] ReadCapturedImages(Stream input, GlbDocument document, bool[] needed,
            int binaryLength, ref long cursor, byte[] scratch)
        {
            int count = document.images?.Length ?? 0;
            var result = new CapturedImage[count];
            try
            {
                for (int image = 0; image < count; image++)
                {
                    if (!needed[image]) continue; // Uniform RGB/DIRT never allocate an atlas.
                    GlbImage descriptor = document.images[image];
                    if (descriptor == null || descriptor.mimeType != "image/png" || descriptor._m8PreviewHasUri)
                        throw new InvalidDataException("Flower preview accepts embedded PNG only.");
                    GlbBufferView view = ReadView(document, descriptor.bufferView);
                    if (view.buffer != 0 || view.byteStride != 0 || view.byteOffset < 0 || view.byteLength < 33 ||
                        (long)view.byteOffset + view.byteLength > binaryLength)
                        throw new InvalidDataException("Invalid embedded PNG buffer view.");
                    MoveToView(input, view, ref cursor, scratch);
                    // IO packet size is not an image-size limit. Only the
                    // bounded PNG packet is resident; IDAT remains on disk.
                    result[image] = CapturedImage.Read(input, view.byteLength, scratch);
                    cursor += view.byteLength;
                }
                return result;
            }
            catch
            {
                foreach (CapturedImage image in result) image?.Dispose();
                throw;
            }
        }

        private static uint PngUInt32(byte[] bytes, int offset) =>
            (uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16 |
            (uint)bytes[offset + 2] << 8 | bytes[offset + 3];

        private static void MoveToView(Stream input, GlbBufferView view,
            ref long cursor, byte[] scratch)
        {
            if (view.byteOffset < cursor)
                throw new InvalidDataException(
                    "GLB buffer views are not sequential.");
            SkipExactly(input, view.byteOffset - cursor, scratch);
            cursor = view.byteOffset;
        }

        private static void SkipExactly(Stream input, long count,
            byte[] scratch)
        {
            while (count > 0L)
            {
                int requested = (int)Math.Min(count, scratch.Length);
                ReadExactly(input, scratch, 0, requested);
                count -= requested;
            }
        }

        private static void ReadExactly(Stream input, byte[] bytes, int offset,
            int count)
        {
            while (count > 0)
            {
                int read = input.Read(bytes, offset, count);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }

        private static void ValidateView(GlbBufferView view, int count,
            int stride, long binaryLength)
        {
            if (view == null || view.buffer != 0 || (view.byteStride != 0 && view.byteStride != stride) ||
                count < 0 || view.byteOffset < 0 || view.byteLength !=
                checked(count * stride) || view.byteOffset + (long)view.byteLength >
                binaryLength)
                throw new InvalidDataException("Invalid GLB buffer view.");
        }

        private static GlbAccessor ReadAccessor(GlbDocument document, int index)
        {
            if ((uint)index >= document.accessors.Length || document.accessors[index] == null)
                throw new InvalidDataException("Invalid GLB accessor reference.");
            return document.accessors[index];
        }

        private static GlbBufferView ReadView(GlbDocument document, int index)
        {
            if ((uint)index >= document.bufferViews.Length || document.bufferViews[index] == null)
                throw new InvalidDataException("Invalid GLB buffer-view reference.");
            return document.bufferViews[index];
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                throw new EndOfStreamException();
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 |
                bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        private static float ReadSingle(byte[] bytes, int offset) =>
            BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32(bytes,
                offset)));

        private sealed class Tile
        {
            internal readonly string Uri;
            internal readonly long ArchiveBytes;
            internal long EstimatedResidentBytes;
            internal readonly Vector3 OriginUnity;
            internal readonly Bounds Bounds;
            internal bool Loading;
            internal bool Failed;
            internal GameObject Object;
            internal Mesh Mesh;
            internal PreviewGlb Presentation;
            internal MeshCollider Collider;
            internal long ResidentBytes;

            internal Tile(string uri, long archiveBytes, Vector3 originUnity,
                Bounds bounds)
            {
                Uri = uri;
                ArchiveBytes = archiveBytes;
                EstimatedResidentBytes = EstimateResidentBytes(archiveBytes);
                OriginUnity = originUnity;
                Bounds = bounds;
            }
        }

        private readonly struct ModelHit
        {
            internal readonly Vector3 Point;
            internal readonly Vector3 Normal;
            internal readonly Color Color;
            internal readonly bool HasColor;

            internal ModelHit(Vector3 point, Vector3 normal,
                Color color = default, bool hasColor = false)
            {
                Point = point;
                Normal = normal;
                Color = color;
                HasColor = hasColor;
            }
        }

        private struct AnnotationDrag
        {
            internal bool Active;
            internal AnnotationMode Mode;
            internal Vector3 StartWorld;
            internal Vector3 CurrentWorld;
            internal Plane Surface;
            internal Vector3 TangentU;
            internal Vector3 TangentV;

            internal AnnotationDrag(AnnotationMode mode, Vector3 startWorld,
                Plane surface, Vector3 tangentU, Vector3 tangentV)
            {
                Active = true;
                Mode = mode;
                StartWorld = startWorld;
                CurrentWorld = startWorld;
                Surface = surface;
                TangentU = tangentU;
                TangentV = tangentV;
            }
        }

        private readonly struct PackageIndex
        {
            internal readonly List<Tile> Tiles;
            internal readonly Bounds Bounds;
            internal readonly long TotalModelBytes;
            internal readonly long TotalResidentEstimateBytes;
            internal readonly MerkabaSpatialBinding? SpatialBinding;

            internal PackageIndex(List<Tile> tiles, Bounds bounds,
                long totalModelBytes, long totalResidentEstimateBytes,
                MerkabaSpatialBinding? spatialBinding)
            {
                Tiles = tiles;
                Bounds = bounds;
                TotalModelBytes = totalModelBytes;
                TotalResidentEstimateBytes = totalResidentEstimateBytes;
                SpatialBinding = spatialBinding;
            }
        }

        // A disposable embedded-PNG receipt, not an in-memory PNG array. This
        // supports only the current writer's noninterlaced RGBA8/filter-0 PNG.
        // All chunk CRCs are checked while IDAT is copied in bounded packets;
        // inflate writes one row directly into the readable sRGB Unity texture.
        internal sealed class CapturedImage : IDisposable
        {
            internal const long DecodedByteLength =
                4L * MerkabaFlowerMaterialBake.Resolution * MerkabaFlowerMaterialBake.Resolution;
            private static readonly uint[] CrcTable = BuildCrcTable();
            private readonly FileStream _zlib;
            private bool _disposed;

            private CapturedImage()
            {
                string path = Path.Combine(Path.GetTempPath(), "m8-preview-image-" +
                    Guid.NewGuid().ToString("N") + ".zlib");
                _zlib = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                    FileShare.None, 4096, FileOptions.DeleteOnClose);
            }

            internal static CapturedImage Read(Stream input, int byteLength, byte[] scratch)
            {
                var image = new CapturedImage();
                try
                {
                    ReadExactly(input, scratch, 0, 8);
                    if (ReadUInt32(scratch, 0) != 0x474e5089u || ReadUInt32(scratch, 4) != 0x0a1a0a0du)
                        throw new InvalidDataException("Invalid embedded PNG signature.");
                    long remaining = byteLength - 8L;
                    bool header = false, srgb = false, idat = false, end = false;
                    while (remaining >= 12L && !end)
                    {
                        ReadExactly(input, scratch, 0, 8);
                        uint length = PngUInt32(scratch, 0), type = ReadUInt32(scratch, 4);
                        if (length > remaining - 12L)
                            throw new InvalidDataException("Embedded PNG chunk exceeds its buffer view.");
                        uint crc = UpdateCrc(uint.MaxValue, scratch, 4, 4);
                        bool copy = false;
                        switch (type)
                        {
                            case 0x52444849u: // IHDR
                                if (header || length != 13u)
                                    throw new InvalidDataException("Invalid Flower PNG header order.");
                                header = true;
                                break;
                            case 0x42475273u: // sRGB
                                if (!header || idat || srgb || length != 1u)
                                    throw new InvalidDataException("Invalid Flower PNG sRGB metadata.");
                                srgb = true;
                                break;
                            case 0x54414449u: // IDAT
                                if (!header) throw new InvalidDataException("PNG data precedes IHDR.");
                                copy = idat = true;
                                break;
                            case 0x444e4549u: // IEND
                                if (!idat || length != 0u)
                                    throw new InvalidDataException("Incomplete Flower PNG image.");
                                end = true;
                                break;
                            default:
                                throw new InvalidDataException("Unsupported chunk in writer-produced Flower PNG.");
                        }
                        long payload = length;
                        while (payload > 0)
                        {
                            int count = (int)Math.Min(payload, scratch.Length);
                            ReadExactly(input, scratch, 0, count);
                            crc = UpdateCrc(crc, scratch, 0, count);
                            if (type == 0x52444849u &&
                                (PngUInt32(scratch, 0) != MerkabaFlowerMaterialBake.Resolution ||
                                 PngUInt32(scratch, 4) != MerkabaFlowerMaterialBake.Resolution ||
                                 scratch[8] != 8 || scratch[9] != 6 || scratch[10] != 0 ||
                                 scratch[11] != 0 || scratch[12] != 0))
                                throw new InvalidDataException("Unsupported Flower PNG dimensions/format.");
                            if (type == 0x42475273u && scratch[0] > 3)
                                throw new InvalidDataException("Invalid PNG rendering intent.");
                            if (copy) image._zlib.Write(scratch, 0, count);
                            payload -= count;
                        }
                        ReadExactly(input, scratch, 0, 4);
                        if (PngUInt32(scratch, 0) != ~crc)
                            throw new InvalidDataException("Embedded PNG chunk CRC mismatch.");
                        remaining -= 12L + length;
                    }
                    if (!end || remaining != 0 || image._zlib.Length < 6L)
                        throw new InvalidDataException("Incomplete or trailing Flower PNG data.");
                    image._zlib.Flush();
                    return image;
                }
                catch { image.Dispose(); throw; }
            }

            internal void Decode(Texture2D texture)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CapturedImage));
                int n = MerkabaFlowerMaterialBake.Resolution;
                if (texture.width != n || texture.height != n || texture.format != TextureFormat.RGBA32)
                    throw new InvalidDataException("Flower PNG target does not match its decoded byte budget.");
                _zlib.Position = 0;
                int cmf = _zlib.ReadByte(), flags = _zlib.ReadByte();
                if ((cmf & 15) != 8 || (cmf >> 4) > 7 || flags < 0 || (flags & 32) != 0 ||
                    ((cmf << 8) | flags) % 31 != 0)
                    throw new InvalidDataException("Unsupported embedded PNG zlib header.");
                var raw = texture.GetRawTextureData<byte>();
                if (raw.Length != DecodedByteLength)
                    throw new InvalidDataException("Flower PNG raw texture capacity mismatch.");
                var row = new byte[1 + 4 * n];
                uint a = 1u, b = 0u;
                using (var encoded = new PngDeflateRange(_zlib, _zlib.Length - 6L))
                using (var inflate = new DeflateStream(encoded, CompressionMode.Decompress, true))
                {
                    for (int y = 0; y < n; y++)
                    {
                        ReadExactly(inflate, row, 0, row.Length);
                        if (row[0] != 0)
                            throw new InvalidDataException("Unsupported filter in writer-produced Flower PNG.");
                        foreach (byte value in row)
                        { a = (a + value) % 65521u; b = (b + a) % 65521u; }
                        // PNG top row / glTF v=0 maps to Unity's last row.
                        // Parser's 1-v and SampleCapture's half-texel taps then
                        // address exactly the writer's sRGB texels.
                        Unity.Collections.NativeArray<byte>.Copy(row, 1, raw, 4 * n * (n - 1 - y), 4 * n);
                    }
                    if (inflate.ReadByte() != -1)
                        throw new InvalidDataException("PNG inflate exceeds the declared texture dimensions.");
                }
                _zlib.Position = _zlib.Length - 4L;
                ReadExactly(_zlib, row, 0, 4);
                if (PngUInt32(row, 0) != ((b << 16) | a))
                    throw new InvalidDataException("PNG decoded Adler32 mismatch.");
                texture.Apply(false, false); // no mip chain; CPU sampling remains readable
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _zlib.Dispose(); // DeleteOnClose removes only this receipt's temp file.
            }

            private static uint[] BuildCrcTable()
            {
                var result = new uint[256];
                for (uint index = 0; index < result.Length; index++)
                {
                    uint value = index;
                    for (int bit = 0; bit < 8; bit++)
                        value = (value >> 1) ^ ((value & 1u) != 0u ? 0xedb88320u : 0u);
                    result[index] = value;
                }
                return result;
            }

            private static uint UpdateCrc(uint crc, byte[] bytes, int offset, int count)
            {
                for (int index = 0; index < count; index++)
                    crc = CrcTable[(crc ^ bytes[offset + index]) & 255u] ^ (crc >> 8);
                return crc;
            }

            private sealed class PngDeflateRange : Stream
            {
                private readonly Stream _source;
                private long _remaining;
                internal PngDeflateRange(Stream source, long length) { _source = source; _remaining = length; }
                public override int Read(byte[] buffer, int offset, int count)
                {
                    int requested = (int)Math.Min(count, _remaining);
                    if (requested == 0) return 0;
                    int read = _source.Read(buffer, offset, requested);
                    if (read == 0) throw new EndOfStreamException("Truncated PNG deflate stream.");
                    _remaining -= read; return read;
                }
                public override bool CanRead => true;
                public override bool CanWrite => false;
                public override bool CanSeek => false;
                public override long Length => throw new NotSupportedException();
                public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
                public override void Flush() { }
                public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                public override void SetLength(long value) => throw new NotSupportedException();
                public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            }
        }

        internal readonly struct ParsedGlb : IDisposable
        {
            internal readonly Vector3[] Positions;
            internal readonly Vector3[] Normals;
            internal readonly Color32[] Colors;
            internal readonly int[] Indices;
            internal readonly Vector2[] Uvs;
            internal readonly ParsedPrimitive[] Primitives;
            internal readonly ParsedMaterial[] Materials;
            internal readonly CapturedImage[] Images;
            internal readonly long DecodedBytes;

            internal ParsedGlb(Vector3[] positions, Vector3[] normals,
                Color32[] colors, int[] indices, Vector2[] uvs, ParsedPrimitive[] primitives,
                ParsedMaterial[] materials, CapturedImage[] images)
            {
                Positions = positions;
                Normals = normals;
                Colors = colors;
                Indices = indices;
                Uvs = uvs; Primitives = primitives; Materials = materials; Images = images;
                DecodedBytes = checked(positions.LongLength * 12L +
                    normals.LongLength * 12L + colors.LongLength * 4L +
                    indices.LongLength * 4L + uvs.LongLength * 8L);
                foreach (CapturedImage image in images)
                    if (image != null) DecodedBytes = checked(DecodedBytes + CapturedImage.DecodedByteLength);
            }

            public void Dispose()
            {
                if (Images == null) return;
                foreach (CapturedImage image in Images) image?.Dispose();
            }
        }

        internal readonly struct ParsedPrimitive
        {
            internal readonly int FirstIndex, IndexCount, Material;
            internal ParsedPrimitive(int first, int count, int material)
            { FirstIndex = first; IndexCount = count; Material = material; }
        }

        internal readonly struct ParsedMaterial
        {
            internal readonly Color ColorFactor;
            internal readonly int Image;
            internal ParsedMaterial(Color factor, int image) { ColorFactor = factor; Image = image; }
        }

        // Unity-owned presentation objects shared by the tiled preview and
        // design library. Decoding/parsing above remains safe off-thread;
        // this constructor and disposal run only on Unity's main thread.
        internal sealed class PreviewGlb : IDisposable
        {
            internal readonly Mesh Mesh;
            internal readonly Material[] Materials;
            private readonly Texture2D[] _textures;
            private readonly ParsedPrimitive[] _primitives;
            private readonly ParsedMaterial[] _definitions;
            private readonly Material[] _owned;
            private Material[] _ghostOwned, _ghostBindings;
            private bool _disposed;

            internal static PreviewGlb Create(ParsedGlb parsed, Material template, string name) => new(parsed, template, name);

            private PreviewGlb(ParsedGlb parsed, Material template, string name)
            {
                if (template == null) throw new ArgumentNullException(nameof(template));
                if (!FitsResidentBudget(EstimateResidentBytes(parsed.DecodedBytes), ResidentDecodedBudgetBytes()))
                    throw new InvalidDataException("Decoded GLB exceeds the device preview residency budget.");
                _primitives = parsed.Primitives; _definitions = parsed.Materials;
                _textures = new Texture2D[parsed.Images.Length];
                _owned = new Material[_definitions.Length];
                Mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                try
                {
                    Mesh.vertices = parsed.Positions; Mesh.normals = parsed.Normals;
                    Mesh.colors32 = parsed.Colors; Mesh.triangles = parsed.Indices;
                    if (parsed.Uvs.Length != 0) Mesh.uv = parsed.Uvs;
                    Mesh.RecalculateBounds();
                    Mesh.subMeshCount = _primitives.Length;
                    for (int primitive = 0; primitive < _primitives.Length; primitive++)
                    {
                        ParsedPrimitive range = _primitives[primitive];
                        Mesh.SetSubMesh(primitive, new SubMeshDescriptor(range.FirstIndex, range.IndexCount, MeshTopology.Triangles)
                        { bounds = Mesh.bounds, firstVertex = 0, vertexCount = Mesh.vertexCount }, MeshUpdateFlags.DontRecalculateBounds);
                    }
                    for (int image = 0; image < _textures.Length; image++)
                    {
                        CapturedImage png = parsed.Images[image];
                        if (png == null) continue;
                        if (SystemInfo.maxTextureSize < MerkabaFlowerMaterialBake.Resolution)
                            throw new InvalidDataException("Device cannot represent the captured Flower texture.");
                        // The writer's base-color PNG stores sRGB. Sampling
                        // must decode it to linear captured radiance, not albedo.
                        var texture = new Texture2D(MerkabaFlowerMaterialBake.Resolution,
                            MerkabaFlowerMaterialBake.Resolution, TextureFormat.RGBA32, false, false)
                        { name = name + " Captured " + image, hideFlags = HideFlags.DontSave,
                            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                        _textures[image] = texture;
                        png.Decode(texture);
                    }
                    Materials = CreateBindings(template, _owned);
                }
                catch { Dispose(); throw; }
            }

            internal void ApplyStyle(Material template)
            {
                for (int index = 0; index < _owned.Length; index++) ApplyMaterial(_owned[index], template, index);
            }

            internal Material[] GhostMaterials(Material template)
            {
                if (_ghostBindings != null) return _ghostBindings;
                _ghostOwned = new Material[_definitions.Length];
                try { return _ghostBindings = CreateBindings(template, _ghostOwned); }
                catch
                {
                    foreach (Material material in _ghostOwned) Release(material);
                    _ghostOwned = null; throw;
                }
            }

            private Material[] CreateBindings(Material template, Material[] owned)
            {
                for (int index = 0; index < owned.Length; index++)
                {
                    owned[index] = new Material(template) { name = template.name + " GLB " + index,
                        hideFlags = HideFlags.DontSave };
                    ApplyMaterial(owned[index], template, index);
                }
                var bindings = new Material[_primitives.Length];
                for (int primitive = 0; primitive < bindings.Length; primitive++)
                    bindings[primitive] = owned[_primitives[primitive].Material];
                return bindings;
            }

            private void ApplyMaterial(Material material, Material template, int index)
            {
                material.CopyPropertiesFromMaterial(template);
                ParsedMaterial source = _definitions[index];
                material.SetColor(CapturedColorFactorId, source.ColorFactor);
                material.SetTexture(BaseMapId, source.Image >= 0 ? _textures[source.Image] : Texture2D.whiteTexture);
            }

            internal Color SampleCapture(int triangle, Vector2 uv)
            {
                int first = 0, last = _primitives.Length - 1;
                long index = 3L * triangle;
                while (first <= last)
                {
                    int middle = first + (last - first) / 2;
                    ParsedPrimitive range = _primitives[middle];
                    if (index < range.FirstIndex) { last = middle - 1; continue; }
                    if (index >= (long)range.FirstIndex + range.IndexCount) { first = middle + 1; continue; }
                    ParsedMaterial source = _definitions[range.Material];
                    Color sample = source.Image < 0 ? Color.white : SampleCapturedTexture(_textures[source.Image], uv);
                    return source.ColorFactor * sample;
                }
                return Color.white;
            }

            private static Color SampleCapturedTexture(Texture2D texture, Vector2 uv)
            {
                // Match the raster sampler: normalized coordinates address
                // texel CENTERS at (i+0.5)/size. Decode each sRGB tap before
                // filtering, never the already blended encoded RGB value.
                float x = Mathf.Clamp01(uv.x) * texture.width - 0.5f;
                float y = Mathf.Clamp01(uv.y) * texture.height - 0.5f;
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                float tx = x - ix, ty = y - iy;
                int x0 = Mathf.Clamp(ix, 0, texture.width - 1), x1 = Mathf.Clamp(ix + 1, 0, texture.width - 1);
                int y0 = Mathf.Clamp(iy, 0, texture.height - 1), y1 = Mathf.Clamp(iy + 1, 0, texture.height - 1);
                Color a = texture.GetPixel(x0, y0).linear, b = texture.GetPixel(x1, y0).linear;
                Color c = texture.GetPixel(x0, y1).linear, d = texture.GetPixel(x1, y1).linear;
                return Color.LerpUnclamped(Color.LerpUnclamped(a, b, tx), Color.LerpUnclamped(c, d, tx), ty);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Release(Mesh);
                foreach (Material material in _owned) Release(material);
                if (_ghostOwned != null) foreach (Material material in _ghostOwned) Release(material);
                foreach (Texture2D texture in _textures) Release(texture);
            }

            private static void Release(UnityEngine.Object value)
            {
                if (value == null) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(value);
                else UnityEngine.Object.DestroyImmediate(value);
            }
        }

        [Serializable]
        private sealed class TilesetDocument
        {
            public TilesetAsset asset;
            public TilesetNode root;
        }

        [Serializable]
        private sealed class TilesetAsset
        {
            public TilesetExtras extras;
        }

        [Serializable]
        private sealed class TilesetExtras
        {
            public TilesetSpatialBinding questMerkabaSpatialBinding;
        }

        [Serializable]
        private sealed class TilesetSpatialBinding
        {
            public int version;
            public string anchorUuid;
            public float[] anchorFromPackage;
        }

        [Serializable]
        private sealed class TilesetNode
        {
            public TilesetBoundingVolume boundingVolume;
            public float[] transform;
            public TilesetContent content;
            public TilesetNode[] children;
        }

        [Serializable]
        private sealed class TilesetBoundingVolume
        {
            public float[] box;
        }

        [Serializable]
        private sealed class TilesetContent
        {
            public string uri;
        }

        [Serializable]
        private sealed class GlbDocument
        {
            public GlbBufferView[] bufferViews;
            public GlbAccessor[] accessors;
            public GlbMesh[] meshes;
            public GlbMaterial[] materials;
            public GlbTexture[] textures;
            public GlbImage[] images;
        }

        [Serializable]
        private sealed class GlbMesh { public GlbPrimitive[] primitives; }

        [Serializable]
        private sealed class GlbPrimitive
        {
            public GlbAttributes attributes;
            public int indices = -1;
            public int mode = 4;
            public int material = -1;
        }

        [Serializable]
        private sealed class GlbAttributes
        {
            public int POSITION = -1;
            public int NORMAL = -1;
            public int COLOR_0 = -1;
            public int TEXCOORD_0 = -1;
        }

        [Serializable]
        private sealed class GlbMaterial { public GlbPbr pbrMetallicRoughness; }
        [Serializable]
        private sealed class GlbPbr
        {
            public float[] baseColorFactor;
            public GlbTextureInfo baseColorTexture;
            public bool _m8PreviewHasBaseColorTexture;
        }
        [Serializable]
        private sealed class GlbTextureInfo
        {
            public int index = -1;
            public int texCoord;
            public bool _m8PreviewHasIndex;
        }
        [Serializable]
        private sealed class GlbTexture { public int source = -1; public int sampler; }
        [Serializable]
        private sealed class GlbImage
        {
            public int bufferView = -1;
            public string mimeType;
            public bool _m8PreviewHasUri;
        }

        [Serializable]
        private sealed class GlbBufferView
        {
            public int buffer;
            public int byteOffset;
            public int byteLength;
            public int byteStride;
        }

        [Serializable]
        private sealed class GlbAccessor
        {
            public int bufferView = -1;
            public int byteOffset;
            public int componentType;
            public int count;
            public string type;
            public bool normalized;
        }

        private enum AnnotationMode
        {
            Off,
            Point,
            Line,
            Plane,
            Select,
            Move
        }

        private enum ModelGrabMode
        {
            None,
            OneHand,
            TwoHand
        }

        private readonly struct AnnotationPoseGrab
        {
            internal readonly int AnnotationId;
            internal readonly Vector3 ControllerPosition;
            internal readonly Quaternion ControllerRotation;
            internal readonly Vector3[] WorldPoints;

            internal AnnotationPoseGrab(int annotationId,
                Vector3 controllerPosition, Quaternion controllerRotation,
                Vector3[] worldPoints)
            {
                AnnotationId = annotationId;
                ControllerPosition = controllerPosition;
                ControllerRotation = controllerRotation;
                WorldPoints = worldPoints;
            }
        }

        [Serializable]
        private sealed class AnnotationRecord
        {
            public int id;
            public string type;
            public string note;
            public Vector3[] points;
            public bool styled;
            public Color color;
            public float width;
        }

        [Serializable]
        private sealed class AnnotationFile
        {
            public string format;
            public int version;
            public int nextId;
            public AnnotationRecord[] items;
        }
    }
}
