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
        private const long MinimumResidentDecodedBytes = 256L * 1024L * 1024L;
        private const long MaximumResidentDecodedBytes = 512L * 1024L * 1024L;
        private const uint GlbMagic = 0x46546c67u;
        private const uint JsonChunkType = 0x4e4f534au;
        private const uint BinaryChunkType = 0x004e4942u;

        [SerializeField] internal Shader previewShader;
        [SerializeField] private float initialPreviewSize = 0.65f;
        [SerializeField] private float initialPreviewDistance = 0.9f;
        [SerializeField] private float minimumPreviewScale = 0.025f;

        private MerkabaExporter _exporter;
        private RoomScanner _scanner;
        private ControllerRayDriver _rayDriver;
        private Transform _modelRoot;
        private Transform _annotationRoot;
        private Material _modelMaterial;
        private Material _annotationMaterial;
        private readonly List<Tile> _tiles = new();
        private readonly List<AnnotationRecord> _annotations = new();
        private readonly List<Vector3> _pendingPoints = new();
        private readonly List<(Tile Tile, float Score)> _desiredTiles = new();
        private readonly HashSet<Tile> _keptTiles = new();
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private bool _loadPending;
        private bool _displaySuppressed;
        private bool _savedReadoutEnabled;
        private bool _savedFineMode;
        private TouchScreenKeyboard _noteKeyboard;
        private string _noteBeforeKeyboard = string.Empty;
        private int _generation;
        private int _nextAnnotationId = 1;
        private float _nextResidencyRefresh;
        private Vector3 _scanCenter;
        private AnnotationMode _annotationMode;
        private int _selectedAnnotationId;
        private Vector3 _moveStart;
        private Vector3[] _moveOriginalPoints;
        private long _totalArchiveBytes;

        public bool IsOpen { get; private set; }
        public string Status { get; private set; } = "GLB View closed";
        public string AnnotationModeText => _annotationMode.ToString().ToUpperInvariant();
        public bool HasSelectedAnnotation => FindSelectedAnnotation() != null;
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
            if (IsOpen || _loadPending) return IsOpen;
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
            _loadPending = true;
            Status = "Opening exported 3D Tiles…";
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
                if (generation == _generation) _loadPending = false;
            }
        }

        public void Close()
        {
            bool wasOpen = IsOpen;
            ++_generation;
            _loadPending = false;
            IsOpen = false;
            _annotationMode = AnnotationMode.Off;
            _selectedAnnotationId = 0;
            _moveOriginalPoints = null;
            CloseNoteKeyboard();
            _pendingPoints.Clear();
            _annotations.Clear();
            foreach (Tile tile in _tiles) DestroyTile(tile);
            _tiles.Clear();
            _totalArchiveBytes = 0L;
            if (_modelRoot != null) Destroy(_modelRoot.gameObject);
            _modelRoot = null;
            _annotationRoot = null;
            if (_modelMaterial != null) Destroy(_modelMaterial);
            if (_annotationMaterial != null) Destroy(_annotationMaterial);
            _modelMaterial = null;
            _annotationMaterial = null;
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
            _annotationMode = (AnnotationMode)(((int)_annotationMode + 1) %
                Enum.GetValues(typeof(AnnotationMode)).Length);
            _pendingPoints.Clear();
            _moveOriginalPoints = null;
            RefreshTileColliders();
            Status = "Annotation " + AnnotationModeText;
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
            _totalArchiveBytes = package.TotalArchiveBytes;

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
            _modelMaterial.SetColor("_BaseColor", Color.white);
            _annotationMaterial = new Material(previewShader)
            {
                name = "Merkaba GLB Annotations",
                hideFlags = HideFlags.DontSave
            };
            _annotationMaterial.SetColor("_BaseColor",
                new Color(1f, 0.35f, 0.05f, 1f));

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
        }

        private void HandleViewerInput()
        {
            _rayDriver ??= FindAnyObjectByType<ControllerRayDriver>();
            if (_rayDriver != null && OVRInput.Get(
                    OVRInput.Button.SecondaryHandTrigger) &&
                _rayDriver.TryGetWorldRay(out Vector3 moveOrigin,
                    out Vector3 moveDirection))
            {
                _modelRoot.position = moveOrigin + moveDirection *
                    initialPreviewDistance;
                return;
            }

            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
            if (Mathf.Abs(stick.x) > 0.12f)
                _modelRoot.Rotate(Vector3.up,
                    -stick.x * 70f * Time.unscaledDeltaTime, Space.World);
            if (Mathf.Abs(stick.y) > 0.12f)
            {
                float scale = Mathf.Clamp(_modelRoot.localScale.x *
                    Mathf.Exp(stick.y * Time.unscaledDeltaTime), 0.002f, 2f);
                _modelRoot.localScale = Vector3.one * scale;
            }

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
            else if (triggerDown && _annotationMode != AnnotationMode.Off)
                AddAnnotationPoint(ray);
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
        }

        private void AddAnnotationPoint(Ray ray)
        {
            if (!TryHitModel(ray, out Vector3 hit))
            {
                Status = "No exported surface under pointer";
                return;
            }
            Vector3 scanPoint = _modelRoot.InverseTransformPoint(hit) +
                _scanCenter;
            _pendingPoints.Add(scanPoint);
            int required = _annotationMode == AnnotationMode.Point ? 1 :
                _annotationMode == AnnotationMode.Line ? 2 : 3;
            if (_pendingPoints.Count < required)
            {
                Status = $"{AnnotationModeText}: " +
                    $"{_pendingPoints.Count}/{required} points";
                return;
            }
            var annotation = new AnnotationRecord
            {
                id = _nextAnnotationId++,
                type = _annotationMode.ToString().ToLowerInvariant(),
                note = string.Empty,
                points = _pendingPoints.ToArray()
            };
            _annotations.Add(annotation);
            _selectedAnnotationId = annotation.id;
            _pendingPoints.Clear();
            RefreshAnnotationObjects();
            Status = $"Added {annotation.type} #{annotation.id}";
        }

        private void BeginMove(Ray ray)
        {
            AnnotationRecord selected = FindNearestAnnotation(ray);
            if (selected == null || !TryHitModel(ray, out _moveStart))
            {
                Status = "Point at an annotation on the exported surface";
                return;
            }
            _selectedAnnotationId = selected.id;
            _moveOriginalPoints = (Vector3[])selected.points.Clone();
            RefreshAnnotationObjects();
            Status = $"Moving {selected.type} #{selected.id}";
        }

        private void ContinueMove(Ray ray)
        {
            if (_moveOriginalPoints == null ||
                !TryHitModel(ray, out Vector3 hit)) return;
            AnnotationRecord selected = FindSelectedAnnotation();
            if (selected == null) return;
            Vector3 deltaWorld = hit - _moveStart;
            Vector3 deltaScan = _modelRoot.InverseTransformVector(deltaWorld);
            selected.points = new Vector3[_moveOriginalPoints.Length];
            for (int index = 0; index < selected.points.Length; index++)
                selected.points[index] = _moveOriginalPoints[index] + deltaScan;
            RefreshAnnotationObjects();
        }

        private bool TryHitModel(Ray ray, out Vector3 point)
        {
            float nearest = float.PositiveInfinity;
            point = default;
            bool found = false;
            foreach (Tile tile in _tiles)
            {
                if (tile.Collider == null ||
                    !tile.Collider.Raycast(ray, out RaycastHit hit, 20f) ||
                    hit.distance >= nearest) continue;
                nearest = hit.distance;
                point = hit.point;
                found = true;
            }
            return found;
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
            float inverseScale = 1f / Mathf.Max(_modelRoot.localScale.x, 1e-5f);
            foreach (AnnotationRecord annotation in _annotations)
            {
                Vector3[] points = annotation.points;
                if (points == null || points.Length == 0) continue;
                Color displayColor = annotation.id == _selectedAnnotationId
                    ? new Color(0.15f, 0.95f, 1f, 1f)
                    : new Color(1f, 0.35f, 0.05f, 1f);
                if (annotation.type == "point")
                {
                    GameObject marker = GameObject.CreatePrimitive(
                        PrimitiveType.Sphere);
                    marker.name = $"Point {annotation.id}";
                    Destroy(marker.GetComponent<Collider>());
                    marker.transform.SetParent(_annotationRoot, false);
                    marker.transform.localPosition = points[0] - _scanCenter;
                    marker.transform.localScale = Vector3.one *
                        (0.018f * inverseScale);
                    MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = _annotationMaterial;
                    var properties = new MaterialPropertyBlock();
                    properties.SetColor("_BaseColor", displayColor);
                    renderer.SetPropertyBlock(properties);
                }
                else
                {
                    var lineObject = new GameObject(
                        $"{annotation.type} {annotation.id}");
                    lineObject.transform.SetParent(_annotationRoot, false);
                    var line = lineObject.AddComponent<LineRenderer>();
                    line.sharedMaterial = _annotationMaterial;
                    line.startColor = line.endColor = Color.white;
                    var properties = new MaterialPropertyBlock();
                    properties.SetColor("_BaseColor", displayColor);
                    line.SetPropertyBlock(properties);
                    line.useWorldSpace = false;
                    line.startWidth = line.endWidth = 0.008f * inverseScale;
                    line.positionCount = annotation.type == "plane"
                        ? points.Length + 1 : points.Length;
                    for (int pointIndex = 0; pointIndex < points.Length;
                         pointIndex++)
                        line.SetPosition(pointIndex,
                            points[pointIndex] - _scanCenter);
                    if (annotation.type == "plane")
                        line.SetPosition(points.Length, points[0] - _scanCenter);
                }
            }
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
            if (!IsOpen || _loadPending || _tiles.Count == 0) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            Vector3 pointerOrigin = default;
            Vector3 pointerDirection = default;
            bool hasPointer = _rayDriver != null &&
                _rayDriver.TryGetWorldRay(out pointerOrigin,
                    out pointerDirection);
            var pointerRay = new Ray(pointerOrigin, pointerDirection);
            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
            _desiredTiles.Clear();
            foreach (Tile tile in _tiles)
            {
                Bounds worldBounds = TransformBounds(_modelRoot,
                    Centered(tile.Bounds));
                if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, worldBounds))
                    continue;
                float score;
                if (hasPointer && worldBounds.IntersectRay(pointerRay,
                        out float pointerDistance))
                    score = -1000000f + pointerDistance;
                else
                    score = Vector3.SqrMagnitude(worldBounds.center -
                        camera.transform.position);
                _desiredTiles.Add((tile, score));
            }
            _desiredTiles.Sort((left, right) =>
            {
                int score = left.Score.CompareTo(right.Score);
                return score != 0 ? score : string.CompareOrdinal(
                    left.Tile.Uri, right.Tile.Uri);
            });
            long budget = ResidentDecodedBudgetBytes();
            _keptTiles.Clear();
            long used = 0L;
            foreach ((Tile tile, _) in _desiredTiles)
            {
                long bytes = tile.ResidentBytes > 0L
                    ? tile.ResidentBytes : tile.ArchiveBytes;
                if (_keptTiles.Count != 0 &&
                    used + bytes > budget)
                    continue;
                _keptTiles.Add(tile);
                used = checked(used + bytes);
            }
            foreach (Tile tile in _tiles)
                if (tile.Object != null && !_keptTiles.Contains(tile))
                    DestroyTile(tile);
            foreach ((Tile tile, _) in _desiredTiles)
                if (_keptTiles.Contains(tile) && tile.Object == null &&
                    !tile.Failed)
                {
                    _ = LoadTileAsync(tile, _generation);
                    break;
                }
            Status = $"GLB View · {LoadedTileCount}/{_tiles.Count} tiles · " +
                $"{FormatBytes(ResidentDecodedBytes())} resident / " +
                $"{FormatBytes(_totalArchiveBytes)} package";
        }

        private static long ResidentDecodedBudgetBytes()
        {
            long systemBytes = Math.Max(0L, (long)SystemInfo.systemMemorySize) *
                1024L * 1024L;
            long adaptive = systemBytes > 0L
                ? systemBytes / 12L : MinimumResidentDecodedBytes;
            return Math.Min(MaximumResidentDecodedBytes,
                Math.Max(MinimumResidentDecodedBytes, adaptive));
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

        private Bounds Centered(Bounds source) =>
            new(source.center - _scanCenter, source.size);

        private async Task LoadTileAsync(Tile tile, int generation)
        {
            if (_loadPending || tile.Loading || tile.Object != null) return;
            tile.Loading = true;
            _loadPending = true;
            try
            {
                ParsedGlb parsed = await Task.Run(() => ReadGlbTile(
                    _exporter.ViewerPackagePath, tile, _scanCenter));
                if (generation != _generation || !IsOpen) return;
                CreateTileObject(tile, parsed);
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
                if (generation == _generation) _loadPending = false;
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
            var filter = tileObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = tileObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _modelMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            tile.Object = tileObject;
            tile.Mesh = mesh;
            tile.ResidentBytes = parsed.DecodedBytes;
            if (TileCollidersRequired())
            {
                tile.Collider = tileObject.AddComponent<MeshCollider>();
                tile.Collider.sharedMesh = mesh;
            }
        }

        private void RefreshTileColliders()
        {
            bool required = TileCollidersRequired();
            foreach (Tile tile in _tiles)
            {
                if (tile.Object == null) continue;
                if (required && tile.Collider == null)
                {
                    tile.Collider = tile.Object.AddComponent<MeshCollider>();
                    tile.Collider.sharedMesh = tile.Mesh;
                }
                else if (!required && tile.Collider != null)
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
                Matrix4x4.identity);
            long totalArchiveBytes = 0L;
            foreach (Tile tile in tiles)
                totalArchiveBytes = checked(totalArchiveBytes + tile.ArchiveBytes);
            return new PackageIndex(tiles, bounds, totalArchiveBytes);
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

        private static ParsedGlb ReadGlbTile(string archivePath, Tile tile,
            Vector3 scanCenter)
        {
            using var stream = new FileStream(archivePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.RandomAccess);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            ZipArchiveEntry entry = archive.GetEntry(tile.Uri) ??
                throw new InvalidDataException("Missing tile " + tile.Uri);
            using Stream entryStream = entry.Open();
            using var input = new BufferedStream(entryStream, 1024 * 1024);
            return ParseGlbForPreview(input, entry.Length, tile.OriginUnity,
                scanCenter);
        }

        internal static ParsedGlb ParseGlbForPreview(byte[] bytes,
            Vector3 originUnity, Vector3 scanCenter)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var input = new MemoryStream(bytes, false);
            return ParseGlbForPreview(input, bytes.LongLength, originUnity,
                scanCenter);
        }

        internal static ParsedGlb ParseGlbForPreview(Stream input,
            long streamLength, Vector3 originUnity, Vector3 scanCenter)
        {
            if (input == null || !input.CanRead)
                throw new ArgumentException("GLB input must be readable.",
                    nameof(input));
            if (streamLength < 28L)
                throw new InvalidDataException("Invalid GLB header.");
            byte[] scratch = new byte[12];
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
            for (int vertex = 0; vertex < position.count; vertex++)
            {
                ReadExactly(input, scratch, 0, 12);
                Vector3 glb = new(ReadSingle(scratch, 0),
                    ReadSingle(scratch, 4), ReadSingle(scratch, 8));
                positions[vertex] = originUnity +
                    new Vector3(-glb.x, glb.y, glb.z) - scanCenter;
            }
            binaryCursor += document.bufferViews[0].byteLength;
            MoveToView(input, document.bufferViews[1], ref binaryCursor,
                scratch);
            for (int vertex = 0; vertex < normal.count; vertex++)
            {
                ReadExactly(input, scratch, 0, 12);
                Vector3 glbNormal = new(ReadSingle(scratch, 0),
                    ReadSingle(scratch, 4), ReadSingle(scratch, 8));
                normals[vertex] = new Vector3(-glbNormal.x, glbNormal.y,
                    glbNormal.z).normalized;
            }
            binaryCursor += document.bufferViews[1].byteLength;
            MoveToView(input, document.bufferViews[2], ref binaryCursor,
                scratch);
            for (int vertex = 0; vertex < color.count; vertex++)
            {
                ReadExactly(input, scratch, 0, 4);
                colors[vertex] = new Color32(scratch[0], scratch[1],
                    scratch[2], scratch[3]);
            }
            binaryCursor += document.bufferViews[2].byteLength;
            MoveToView(input, document.bufferViews[3], ref binaryCursor,
                scratch);
            for (int value = 0; value < index.count; value++)
            {
                ReadExactly(input, scratch, 0, 4);
                uint read = ReadUInt32(scratch, 0);
                if (read >= position.count)
                    throw new InvalidDataException("GLB index out of range.");
                indices[value] = (int)read;
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
                OriginUnity = originUnity;
                Bounds = bounds;
            }
        }

        private readonly struct PackageIndex
        {
            internal readonly List<Tile> Tiles;
            internal readonly Bounds Bounds;
            internal readonly long TotalArchiveBytes;

            internal PackageIndex(List<Tile> tiles, Bounds bounds,
                long totalArchiveBytes)
            {
                Tiles = tiles;
                Bounds = bounds;
                TotalArchiveBytes = totalArchiveBytes;
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
