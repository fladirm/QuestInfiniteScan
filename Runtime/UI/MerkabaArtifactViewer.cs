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
        private const uint GlbMagic = 0x46546c67u;
        private const uint JsonChunkType = 0x4e4f534au;
        private const uint BinaryChunkType = 0x004e4942u;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SourceBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DestinationBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaDitherId =
            Shader.PropertyToID("_AlphaDither");

        [SerializeField] internal Shader previewShader;
        [SerializeField] private float initialPreviewSize = 0.65f;
        [SerializeField] private float initialPreviewDistance = 0.9f;
        [SerializeField] private float minimumPreviewScale = 0.025f;
        [SerializeField] private Color backdropColor =
            new(0.025f, 0.04f, 0.065f, 0.42f);

        private MerkabaExporter _exporter;
        private RoomScanner _scanner;
        private ControllerRayDriver _rayDriver;
        private Transform _modelRoot;
        private Transform _annotationRoot;
        private GameObject _backdrop;
        private GameObject _continuationMarker;
        private Material _modelMaterial;
        private Material _annotationMaterial;
        private Material _backdropMaterial;
        private Material _continuationMaterial;
        private Mesh _pointMarkerMesh;
        private Mesh _continuationMesh;
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
        private bool _worldLocked = true;
        private bool _roomAligned;
        private bool _hasSavedPreviewTransform;
        private TouchScreenKeyboard _noteKeyboard;
        private string _noteBeforeKeyboard = string.Empty;
        private int _generation;
        private int _tileLoadsInFlight;
        private int _nextAnnotationId = 1;
        private float _nextResidencyRefresh;
        private float _previewOpacity = 1f;
        private float _grabDistance;
        private Vector3 _scanCenter;
        private Vector3 _grabOffsetInRayFrame;
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
        private long _totalModelBytes;
        private long _totalResidentEstimateBytes;

        public bool IsOpen { get; private set; }
        public string Status { get; private set; } = "GLB View closed";
        public string AnnotationModeText => _annotationMode.ToString().ToUpperInvariant();
        public bool HasSelectedAnnotation => FindSelectedAnnotation() != null;
        public bool WorldLocked
        {
            get => _worldLocked;
            set
            {
                if (_worldLocked == value) return;
                if (_roomAligned && !value) SetRoomAligned(false);
                _worldLocked = value;
                ApplyViewerFrame();
            }
        }
        public bool RoomAligned
        {
            get => _roomAligned;
            set => SetRoomAligned(value);
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
                if (selected != null) selected.note = value ?? string.Empty;
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

        private string AnnotationPath => Path.Combine(
            Path.GetDirectoryName(_exporter.ViewerPackagePath),
            "QuestMerkabaScan.annotations.json");

        private void Awake()
        {
            _exporter = GetComponent<MerkabaExporter>();
            _scanner = GetComponent<RoomScanner>();
            _rayDriver = FindAnyObjectByType<ControllerRayDriver>();
        }

        private void Update()
        {
            PollNoteKeyboard();
            if (!IsOpen || _modelRoot == null) return;
            if (_scanner.IsScanning || _scanner.IsScanStarting)
            {
                Close();
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
        private void OnDestroy() => Close();

        private void OnApplicationPause(bool paused)
        {
            if (paused) CancelTransientInput();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) CancelTransientInput();
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

        public async Task<bool> OpenAsync()
        {
            if (IsOpen || _indexLoadPending) return IsOpen;
            if (previewShader == null)
            {
                Status = "GLB View shader is not wired";
                return false;
            }
            string archivePath = _exporter.ViewerPackagePath;
            if (!File.Exists(archivePath))
            {
                Status = "Export 3D Tiles first";
                return false;
            }

            int generation = ++_generation;
            _indexLoadPending = true;
            Status = "Opening exported 3D Tiles…";
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
                if (generation != _generation || !isActiveAndEnabled)
                    return false;

                PackageIndex package = await Task.Run(() =>
                    ReadPackageIndex(archivePath));
                if (generation != _generation || !isActiveAndEnabled)
                    return false;

                _savedReadoutEnabled = _scanner.ReadoutDrawEnabled;
                _savedFineMode = _scanner.FineMode;
                _scanner.ReadoutDrawEnabled = false;
                _scanner.FineMode = false;
                _displaySuppressed = true;
                CreatePreview(package);
                LoadAnnotations();
                IsOpen = true;
                Status = $"GLB View · 0/{_tiles.Count} tiles";
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

        public void Close()
        {
            bool wasOpen = IsOpen;
            ++_generation;
            _indexLoadPending = false;
            _tileLoadsInFlight = 0;
            IsOpen = false;
            _annotationMode = AnnotationMode.Off;
            _roomAligned = false;
            _hasSavedPreviewTransform = false;
            _selectedAnnotationId = 0;
            _moveOriginalPoints = null;
            CancelTransientInput();
            CloseNoteKeyboard();
            _annotations.Clear();
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
            if (_modelRoot != null) Destroy(_modelRoot.gameObject);
            _modelRoot = null;
            _annotationRoot = null;
            if (_modelMaterial != null) Destroy(_modelMaterial);
            if (_annotationMaterial != null) Destroy(_annotationMaterial);
            if (_backdropMaterial != null) Destroy(_backdropMaterial);
            if (_continuationMaterial != null) Destroy(_continuationMaterial);
            _modelMaterial = null;
            _annotationMaterial = null;
            _backdropMaterial = null;
            _continuationMaterial = null;
            if (_displaySuppressed && _scanner != null)
            {
                _scanner.ReadoutDrawEnabled = _savedReadoutEnabled;
                _scanner.FineMode = _savedFineMode;
            }
            _displaySuppressed = false;
            if (wasOpen) Status = "GLB View closed";
        }

        public void CycleAnnotationMode()
        {
            CancelAnnotationDrag();
            _annotationMode = (AnnotationMode)(((int)_annotationMode + 1) %
                Enum.GetValues(typeof(AnnotationMode)).Length);
            _moveOriginalPoints = null;
            RefreshTileColliders();
            Status = "Annotation " + AnnotationModeText;
        }

        public void DeleteSelectedAnnotation()
        {
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected == null)
            {
                Status = "Select an annotation to delete";
                return;
            }
            CloseNoteKeyboard();
            _annotations.Remove(selected);
            _selectedAnnotationId = 0;
            RefreshAnnotationObjects();
            Status = $"Deleted {selected.type} #{selected.id}";
        }

        public void BeginNoteEdit()
        {
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected == null)
            {
                Status = "Select a point, line or plane first";
                return;
            }
            if (_noteKeyboard != null &&
                _noteKeyboard.status == TouchScreenKeyboard.Status.Visible)
                return;
            _noteBeforeKeyboard = selected.note ?? string.Empty;
            _noteKeyboard = TouchScreenKeyboard.Open(_noteBeforeKeyboard,
                TouchScreenKeyboardType.Default, false, true, false, false,
                "Annotation note", 512);
            Status = _noteKeyboard != null
                ? $"Editing {selected.type} #{selected.id}"
                : "Quest system keyboard is unavailable";
        }

        private void PollNoteKeyboard()
        {
            if (_noteKeyboard == null) return;
            TouchScreenKeyboard.Status keyboardStatus = _noteKeyboard.status;
            if (keyboardStatus == TouchScreenKeyboard.Status.Visible)
            {
                SelectedNote = _noteKeyboard.text;
                return;
            }
            if (keyboardStatus == TouchScreenKeyboard.Status.Canceled)
                SelectedNote = _noteBeforeKeyboard;
            else
                SelectedNote = _noteKeyboard.text;
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected != null)
                Status = keyboardStatus == TouchScreenKeyboard.Status.Canceled
                    ? $"Note unchanged for {selected.type} #{selected.id}"
                    : $"Updated note for {selected.type} #{selected.id}";
            _noteKeyboard = null;
        }

        private void CloseNoteKeyboard()
        {
            if (_noteKeyboard != null)
                _noteKeyboard.active = false;
            _noteKeyboard = null;
            _noteBeforeKeyboard = string.Empty;
        }

        public void SaveAnnotations()
        {
            try
            {
                string directory = Path.GetDirectoryName(AnnotationPath);
                Directory.CreateDirectory(directory);
                string temporary = AnnotationPath + ".tmp";
                var file = new AnnotationFile
                {
                    format = "QuestMerkabaAnnotations",
                    version = 1,
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
                MerkabaFilePublishing.Publish(temporary, AnnotationPath);
                Status = $"Saved {_annotations.Count} annotations";
            }
            catch (Exception exception)
            {
                Logger.Error("Could not save GLB View annotations: " + exception);
                Status = "Annotation save failed: " + exception.Message;
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
            bool rightGrip = OVRInput.Get(
                OVRInput.Button.SecondaryHandTrigger);
            if (!_roomAligned && _rayDriver != null && rightGrip &&
                _rayDriver.TryGetWorldRay(out Vector3 moveOrigin,
                    out Vector3 moveDirection))
            {
                if (!_modelGrabActive)
                    BeginModelGrab(moveOrigin, moveDirection);
                ContinueModelGrab(moveOrigin, moveDirection);
                return;
            }
            _modelGrabActive = false;

            Camera camera = Camera.main;
            Vector2 leftStick = ApplyDeadZone(OVRInput.Get(
                OVRInput.Axis2D.PrimaryThumbstick));
            bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger);
            if (!_roomAligned && leftGrip && Mathf.Abs(leftStick.y) > 0f)
            {
                float scale = Mathf.Clamp(_modelRoot.localScale.x *
                    Mathf.Exp(leftStick.y * ZoomSpeed * Time.unscaledDeltaTime),
                    0.002f, 2f);
                _modelRoot.localScale = Vector3.one * scale;
                RefreshAnnotationObjects();
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

            if (_rayDriver == null || _rayDriver.IsPointingAtUi ||
                !_rayDriver.TryGetWorldRay(out Vector3 origin,
                    out Vector3 direction)) return;
            var ray = new Ray(origin, direction);
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
                if (triggerUp) _moveOriginalPoints = null;
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

        private static Vector2 ApplyDeadZone(Vector2 value)
        {
            float magnitude = value.magnitude;
            if (magnitude <= InputDeadZone) return Vector2.zero;
            float scaled = Mathf.InverseLerp(InputDeadZone, 1f,
                Mathf.Min(1f, magnitude));
            return value / magnitude * scaled;
        }

        private void BeginModelGrab(Vector3 origin, Vector3 direction)
        {
            direction.Normalize();
            BuildRayFrame(direction, out Vector3 right, out Vector3 up);
            Vector3 delta = _modelRoot.position - origin;
            _grabDistance = Mathf.Max(0.05f, Vector3.Dot(delta, direction));
            Vector3 transverse = delta - direction * _grabDistance;
            _grabOffsetInRayFrame = new Vector3(
                Vector3.Dot(transverse, right),
                Vector3.Dot(transverse, up), 0f);
            _modelGrabActive = true;
        }

        private void ContinueModelGrab(Vector3 origin, Vector3 direction)
        {
            direction.Normalize();
            BuildRayFrame(direction, out Vector3 right, out Vector3 up);
            _modelRoot.position = origin + direction * _grabDistance +
                right * _grabOffsetInRayFrame.x +
                up * _grabOffsetInRayFrame.y;
        }

        private static void BuildRayFrame(Vector3 forward, out Vector3 right,
            out Vector3 up)
        {
            right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(Vector3.forward, forward);
            right.Normalize();
            up = Vector3.Cross(forward, right).normalized;
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

        private void SetRoomAligned(bool aligned)
        {
            if (_roomAligned == aligned || _modelRoot == null)
            {
                _roomAligned = aligned && _modelRoot != null;
                return;
            }
            if (aligned)
            {
                if (RoomSpaceRoot.Instance == null ||
                    !RoomSpaceRoot.Instance.IsBound)
                {
                    Status = "ALIGN 1:1 needs the localized scan anchor";
                    return;
                }
                _savedPreviewWorldPosition = _modelRoot.position;
                _savedPreviewWorldRotation = _modelRoot.rotation;
                _savedPreviewScale = _modelRoot.localScale;
                _hasSavedPreviewTransform = true;
                _worldLocked = true;
                _modelRoot.SetParent(RoomSpaceRoot.Instance.transform, false);
                _modelRoot.localPosition = _scanCenter;
                _modelRoot.localRotation = Quaternion.identity;
                _modelRoot.localScale = Vector3.one;
                _roomAligned = true;
                RefreshAnnotationObjects();
                Status = "GLB View aligned 1:1 to the scanned room";
                return;
            }

            _roomAligned = false;
            ApplyViewerFrame();
            if (_hasSavedPreviewTransform)
            {
                _modelRoot.SetPositionAndRotation(_savedPreviewWorldPosition,
                    _savedPreviewWorldRotation);
                _modelRoot.localScale = _savedPreviewScale;
                _hasSavedPreviewTransform = false;
            }
            RefreshAnnotationObjects();
            Status = "GLB View restored to model review";
        }

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
            _modelMaterial.SetFloat(AlphaDitherId,
                _previewOpacity < 0.999f ? 1f : 0f);
            foreach (Tile tile in _tiles)
            {
                MeshRenderer renderer = tile.Object != null
                    ? tile.Object.GetComponent<MeshRenderer>() : null;
                if (renderer != null) renderer.enabled = visible;
            }
        }

        private static void ConfigureMaterial(Material material, Color color,
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

        private void BeginAnnotationDrag(Ray ray)
        {
            if (!TryHitModel(ray, out ModelHit hit))
            {
                Status = "No exported surface under pointer";
                return;
            }
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
            RefreshAnnotationObjects();
            Status = $"Added {annotation.type} #{annotation.id}";
        }

        private Vector3 WorldToScanPoint(Vector3 worldPoint) =>
            _modelRoot.InverseTransformPoint(worldPoint) + _scanCenter;

        private void CancelTransientInput()
        {
            _modelGrabActive = false;
            _moveOriginalPoints = null;
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
            if (!TryHitModel(ray, out ModelHit hit))
            {
                Status = "No exported surface under pointer";
                return;
            }
            var annotation = new AnnotationRecord
            {
                id = _nextAnnotationId++,
                type = "point",
                note = string.Empty,
                points = new[] { WorldToScanPoint(hit.Point) }
            };
            _annotations.Add(annotation);
            _selectedAnnotationId = annotation.id;
            RefreshAnnotationObjects();
            Status = $"Added {annotation.type} #{annotation.id}";
        }

        private void BeginMove(Ray ray)
        {
            AnnotationRecord selected = FindNearestAnnotation(ray);
            if (selected == null || !TryHitModel(ray, out ModelHit hit))
            {
                Status = "Point at an annotation on the exported surface";
                return;
            }
            _moveStart = hit.Point;
            _selectedAnnotationId = selected.id;
            _moveOriginalPoints = (Vector3[])selected.points.Clone();
            RefreshAnnotationObjects();
            Status = $"Moving {selected.type} #{selected.id}";
        }

        private void ContinueMove(Ray ray)
        {
            if (_moveOriginalPoints == null ||
                !TryHitModel(ray, out ModelHit hit)) return;
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected == null) return;
            Vector3 deltaWorld = hit.Point - _moveStart;
            Vector3 deltaScan = _modelRoot.InverseTransformVector(deltaWorld);
            selected.points = new Vector3[_moveOriginalPoints.Length];
            for (int index = 0; index < selected.points.Length; index++)
                selected.points[index] = _moveOriginalPoints[index] + deltaScan;
            UpdateAnnotationObject(selected);
        }

        private bool TryHitModel(Ray ray, out ModelHit modelHit)
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
                    boundsDistance > 20f)
                    continue;
                EnsureTileCollider(tile);
                if (tile.Collider == null || !tile.Collider.Raycast(ray,
                        out RaycastHit hit, 20f) ||
                    hit.distance >= nearest) continue;
                nearest = hit.distance;
                modelHit = new ModelHit(hit.point, hit.normal);
                found = true;
            }
            return found;
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
            float bestDistance = 0.035f;
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
            float inverseScale = 1f / Mathf.Max(_modelRoot.lossyScale.x, 1e-5f);
            bool selected = annotation.id == _selectedAnnotationId;
            Color outlineColor = selected
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
                    (0.010f * inverseScale);
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
                renderer.sharedMaterial = _annotationMaterial;
                Color fill = outlineColor;
                fill.a = selected ? 0.32f : 0.18f;
                ConfigureRenderer(renderer, fill);
            }

            var line = visual.AddComponent<LineRenderer>();
            line.sharedMaterial = _annotationMaterial;
            line.startColor = line.endColor = Color.white;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.startWidth = line.endWidth = 0.0025f * inverseScale;
            line.positionCount = annotation.type == "plane"
                ? points.Length + 1 : points.Length;
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                line.SetPosition(pointIndex, points[pointIndex] - _scanCenter);
            if (annotation.type == "plane")
                line.SetPosition(points.Length, points[0] - _scanCenter);
            var lineProperties = new MaterialPropertyBlock();
            lineProperties.SetColor(BaseColorId, outlineColor);
            line.SetPropertyBlock(lineProperties);
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
            if (annotation.type != "plane") return;
            Mesh mesh = visual.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return;
            mesh.vertices = Array.ConvertAll(points,
                point => point - _scanCenter);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
                    _annotationDraftLine.endWidth = 0.0025f;
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
                    renderer.sharedMaterial = _annotationMaterial;
                    ConfigureRenderer(renderer,
                        new Color(0.15f, 0.95f, 1f, 0.22f));
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
            if (!File.Exists(AnnotationPath))
            {
                RefreshAnnotationObjects();
                return;
            }
            try
            {
                AnnotationFile file = JsonUtility.FromJson<AnnotationFile>(
                    File.ReadAllText(AnnotationPath));
                if (file?.format != "QuestMerkabaAnnotations" ||
                    file.version != 1 || file.items == null) return;
                _annotations.AddRange(file.items);
                _nextAnnotationId = Mathf.Max(file.nextId, 1);
            }
            catch (Exception exception)
            {
                Logger.Warning("Could not load GLB View annotations: " +
                    exception.Message);
            }
            RefreshAnnotationObjects();
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
                if (_keptTiles.Count != 0 &&
                    used + bytes > budget)
                    continue;
                _keptTiles.Add(tile);
                used = checked(used + bytes);
            }
            foreach (Tile tile in _tiles)
                if (tile.Object != null && !_keptTiles.Contains(tile))
                    DestroyTile(tile);
            StartPendingTileLoads();
            UpdateContinuationMarker(camera);
            Status = $"GLB View · {LoadedTileCount}/{_tiles.Count} tiles · " +
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
            try
            {
                ParsedGlb parsed = await Task.Run(() => ReadGlbTile(
                    _exporter.ViewerPackagePath, tile));
                if (generation != _generation || !IsOpen) return;
                if (!_keptTiles.Contains(tile)) return;
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
            var mesh = new Mesh
            {
                name = "Merkaba " + tile.Uri,
                indexFormat = IndexFormat.UInt32
            };
            mesh.vertices = parsed.Positions;
            mesh.normals = parsed.Normals;
            mesh.colors32 = parsed.Colors;
            mesh.triangles = parsed.Indices;
            mesh.RecalculateBounds();

            var tileObject = new GameObject(tile.Uri.Replace('/', '_'));
            tileObject.transform.SetParent(_modelRoot, false);
            tileObject.transform.localPosition = tile.OriginUnity - _scanCenter;
            var filter = tileObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = tileObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _modelMaterial;
            renderer.enabled = _previewOpacity > 0.001f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            tile.Object = tileObject;
            tile.Mesh = mesh;
            tile.ResidentBytes = EstimateResidentBytes(parsed.DecodedBytes);
        }

        private void RefreshTileColliders()
        {
            if (TileCollidersRequired()) return;
            foreach (Tile tile in _tiles)
            {
                if (tile.Object == null) continue;
                if (tile.Collider != null)
                {
                    Destroy(tile.Collider);
                    tile.Collider = null;
                }
            }
        }

        private bool TileCollidersRequired() =>
            _annotationMode == AnnotationMode.Point ||
            _annotationMode == AnnotationMode.Line ||
            _annotationMode == AnnotationMode.Plane ||
            _annotationMode == AnnotationMode.Move;

        private static void DestroyTile(Tile tile)
        {
            if (tile.Object != null) Destroy(tile.Object);
            if (tile.Mesh != null) Destroy(tile.Mesh);
            tile.Object = null;
            tile.Mesh = null;
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
            ZipArchiveEntry manifest = archive.GetEntry("tileset.json") ??
                throw new InvalidDataException("tileset.json is missing.");
            string json;
            using (Stream input = manifest.Open())
            using (var reader = new StreamReader(input, Encoding.UTF8, true,
                       64 * 1024, false))
                json = reader.ReadToEnd();
            TilesetDocument document = JsonUtility.FromJson<TilesetDocument>(json);
            if (document?.root == null)
                throw new InvalidDataException("Invalid 3D Tiles root.");
            var tiles = new List<Tile>();
            CollectTiles(document.root, Matrix4x4.identity, archive, tiles);
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
                totalResidentEstimateBytes);
        }

        private static void CollectTiles(TilesetNode node, Matrix4x4 parent,
            ZipArchive archive, List<Tile> output)
        {
            Matrix4x4 local = ReadColumnMajorMatrix(node.transform);
            Matrix4x4 current = parent * local;
            if (node.content != null && !string.IsNullOrWhiteSpace(
                    node.content.uri))
            {
                string uri = node.content.uri.Replace('\\', '/');
                ZipArchiveEntry entry = archive.GetEntry(uri) ??
                    throw new InvalidDataException("Missing tile " + uri);
                output.Add(new Tile(uri, entry.Length,
                    TilesetToUnity(current.MultiplyPoint3x4(Vector3.zero)),
                    ConvertTilesetBox(node.boundingVolume?.box, current)));
            }
            if (node.children == null) return;
            foreach (TilesetNode child in node.children)
                if (child != null) CollectTiles(child, current, archive, output);
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

        private static ParsedGlb ReadGlbTile(string archivePath, Tile tile)
        {
            using var stream = new FileStream(archivePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.RandomAccess);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            ZipArchiveEntry entry = archive.GetEntry(tile.Uri) ??
                throw new InvalidDataException("Missing tile " + tile.Uri);
            using Stream entryStream = entry.Open();
            using var input = new BufferedStream(entryStream, 1024 * 1024);
            return ParseGlbForPreview(input, entry.Length);
        }

        internal static ParsedGlb ParseGlbForPreview(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var input = new MemoryStream(bytes, false);
            return ParseGlbForPreview(input, bytes.LongLength);
        }

        internal static ParsedGlb ParseGlbForPreview(Stream input,
            long streamLength)
        {
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
            GlbDocument document = JsonUtility.FromJson<GlbDocument>(json);
            if (document?.bufferViews == null ||
                document.bufferViews.Length != 4 || document.accessors == null ||
                document.accessors.Length != 4)
                throw new InvalidDataException("Unsupported GLB layout.");
            GlbAccessor position = document.accessors[0];
            GlbAccessor normal = document.accessors[1];
            GlbAccessor color = document.accessors[2];
            GlbAccessor index = document.accessors[3];
            if (position.componentType != 5126 || position.type != "VEC3" ||
                normal.componentType != 5126 || normal.type != "VEC3" ||
                color.componentType != 5121 || color.type != "VEC4" ||
                !color.normalized || index.componentType != 5125 ||
                index.type != "SCALAR" || position.count != normal.count ||
                position.count != color.count || index.count % 3 != 0)
                throw new InvalidDataException("Unsupported GLB accessor ABI.");
            ValidateView(document.bufferViews[0], position.count, 12,
                binaryLength);
            ValidateView(document.bufferViews[1], normal.count, 12,
                binaryLength);
            ValidateView(document.bufferViews[2], color.count, 4,
                binaryLength);
            ValidateView(document.bufferViews[3], index.count, 4,
                binaryLength);
            var positions = new Vector3[position.count];
            var normals = new Vector3[position.count];
            var colors = new Color32[position.count];
            var indices = new int[index.count];
            long binaryCursor = 0L;
            MoveToView(input, document.bufferViews[0], ref binaryCursor,
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
            binaryCursor += document.bufferViews[0].byteLength;
            MoveToView(input, document.bufferViews[1], ref binaryCursor,
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
            binaryCursor += document.bufferViews[1].byteLength;
            MoveToView(input, document.bufferViews[2], ref binaryCursor,
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
            binaryCursor += document.bufferViews[2].byteLength;
            MoveToView(input, document.bufferViews[3], ref binaryCursor,
                scratch);
            for (int valueBase = 0; valueBase < index.count;)
            {
                int batch = Math.Min(index.count - valueBase,
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
            binaryCursor += document.bufferViews[3].byteLength;
            SkipExactly(input, binaryLength - binaryCursor, scratch);
            for (int triangle = 0; triangle < indices.Length; triangle += 3)
                (indices[triangle + 1], indices[triangle + 2]) =
                    (indices[triangle + 2], indices[triangle + 1]);
            return new ParsedGlb(positions, normals, colors, indices);
        }

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
            if (view == null || view.byteOffset < 0 || view.byteLength !=
                checked(count * stride) || view.byteOffset + (long)view.byteLength >
                binaryLength)
                throw new InvalidDataException("Invalid GLB buffer view.");
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
            internal readonly long EstimatedResidentBytes;
            internal readonly Vector3 OriginUnity;
            internal readonly Bounds Bounds;
            internal bool Loading;
            internal bool Failed;
            internal GameObject Object;
            internal Mesh Mesh;
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

            internal ModelHit(Vector3 point, Vector3 normal)
            {
                Point = point;
                Normal = normal;
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

            internal PackageIndex(List<Tile> tiles, Bounds bounds,
                long totalModelBytes, long totalResidentEstimateBytes)
            {
                Tiles = tiles;
                Bounds = bounds;
                TotalModelBytes = totalModelBytes;
                TotalResidentEstimateBytes = totalResidentEstimateBytes;
            }
        }

        internal readonly struct ParsedGlb
        {
            internal readonly Vector3[] Positions;
            internal readonly Vector3[] Normals;
            internal readonly Color32[] Colors;
            internal readonly int[] Indices;
            internal readonly long DecodedBytes;

            internal ParsedGlb(Vector3[] positions, Vector3[] normals,
                Color32[] colors, int[] indices)
            {
                Positions = positions;
                Normals = normals;
                Colors = colors;
                Indices = indices;
                DecodedBytes = checked(positions.LongLength * 12L +
                    normals.LongLength * 12L + colors.LongLength * 4L +
                    indices.LongLength * 4L);
            }
        }

        [Serializable]
        private sealed class TilesetDocument
        {
            public TilesetNode root;
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
        }

        [Serializable]
        private sealed class GlbBufferView
        {
            public int byteOffset;
            public int byteLength;
        }

        [Serializable]
        private sealed class GlbAccessor
        {
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

        [Serializable]
        private sealed class AnnotationRecord
        {
            public int id;
            public string type;
            public string note;
            public Vector3[] points;
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
