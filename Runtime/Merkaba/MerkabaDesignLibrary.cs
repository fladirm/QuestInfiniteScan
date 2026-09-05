using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Genesis.RoomScan.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    [Serializable]
    public sealed class MerkabaDesignAsset
    {
        public int formatVersion = 1;
        public string id;
        public string displayName;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public string importedUtc;

        internal Bounds Bounds => new(boundsCenter, boundsSize);
    }

    /// <summary>
    /// Content-addressed GLB library for session design objects. Geometry is
    /// decoded only by the existing artifact-viewer decoder and never becomes
    /// canonical M8 state.
    /// </summary>
    internal sealed class MerkabaDesignLibrary
    {
        internal const int FormatVersion = 1;
        private const int CopyBufferBytes = 1024 * 1024;
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private readonly string _root;
        private readonly List<MerkabaDesignAsset> _assets = new();
        private readonly Dictionary<string, Mesh> _meshes = new();
        private readonly Dictionary<int, InstanceVisual> _visuals = new();
        private MerkabaDesignDocument _document;
        private Transform _roomRoot;
        private Transform _objectRoot;
        private Material _objectMaterial;
        private Material _ghostMaterial;
        private GameObject _ghost;
        private string _ghostAssetId;
        private string _selectedAssetId;
        private int _selectedInstanceId;
        private bool _placing;
        private GrabMode _grabMode;
        private OneHandGrab _oneHandGrab;
        private TwoHandGrab _twoHandGrab;
        private Action _changed;

        internal MerkabaDesignLibrary(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Design library root is required.",
                    nameof(root));
            _root = Path.GetFullPath(root);
            Refresh();
        }

        internal IReadOnlyList<MerkabaDesignAsset> Assets => _assets;
        internal string Root => _root;
        internal string SelectedAssetId => _selectedAssetId ?? string.Empty;
        internal int SelectedInstanceId => _selectedInstanceId;
        internal bool PlacementEnabled => _placing;
        internal MerkabaDesignInstance SelectedInstance =>
            _document?.instances?.Find(instance => instance != null &&
                instance.instanceId == _selectedInstanceId);
        internal IReadOnlyList<MerkabaDesignInstance> Instances =>
            _document?.instances ?? (IReadOnlyList<MerkabaDesignInstance>)
                Array.Empty<MerkabaDesignInstance>();

        internal void Open(MerkabaDesignDocument document, Transform roomRoot,
            Shader shader, Action changed)
        {
            CloseRuntime();
            _document = document ?? throw new ArgumentNullException(
                nameof(document));
            _roomRoot = roomRoot ?? throw new ArgumentNullException(
                nameof(roomRoot));
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            _changed = changed;
            var rootObject = new GameObject("Merkaba Design Objects");
            _objectRoot = rootObject.transform;
            _objectRoot.SetParent(_roomRoot, false);
            _objectMaterial = new Material(shader)
            {
                name = "Merkaba Design Objects",
                hideFlags = HideFlags.DontSave
            };
            MerkabaArtifactViewer.ConfigureMaterial(_objectMaterial,
                Color.white, true);
            _ghostMaterial = new Material(shader)
            {
                name = "Merkaba Design Object Ghost",
                hideFlags = HideFlags.DontSave
            };
            MerkabaArtifactViewer.ConfigureMaterial(_ghostMaterial,
                new Color(0.45f, 1f, 0.78f, 0.38f), false);
            foreach (MerkabaDesignInstance instance in _document.instances)
                TryCreateVisual(instance);
            RefreshSelectionVisual();
            if (_assets.Count > 0) SelectAsset(_assets[0].id);
        }

        internal void CloseRuntime()
        {
            EndGrab(true);
            foreach (InstanceVisual visual in _visuals.Values)
                DestroyObject(visual.Root);
            _visuals.Clear();
            if (_ghost != null) DestroyObject(_ghost);
            if (_objectRoot != null) DestroyObject(_objectRoot.gameObject);
            foreach (Mesh mesh in _meshes.Values) DestroyObject(mesh);
            _meshes.Clear();
            if (_objectMaterial != null) DestroyObject(_objectMaterial);
            if (_ghostMaterial != null) DestroyObject(_ghostMaterial);
            _document = null;
            _roomRoot = null;
            _objectRoot = null;
            _objectMaterial = null;
            _ghostMaterial = null;
            _ghost = null;
            _ghostAssetId = null;
            _selectedAssetId = null;
            _selectedInstanceId = 0;
            _placing = false;
            _changed = null;
        }

        internal bool SelectAsset(string assetId)
        {
            if (Find(assetId) == null) return false;
            _selectedAssetId = assetId;
            if (_placing) EnsureGhost();
            return true;
        }

        internal void SetPlacementEnabled(bool enabled)
        {
            _placing = enabled && !string.IsNullOrEmpty(_selectedAssetId);
            if (_placing) EnsureGhost();
            if (_ghost != null) _ghost.SetActive(_placing);
            if (_placing) _selectedInstanceId = 0;
            RefreshSelectionVisual();
            EndGrab(true);
        }

        internal void UpdatePlacementPreview(Ray ray, bool hasSurface,
            Vector3 surfacePoint, Vector3 surfaceNormal, bool surfaceSnap,
            bool uprightSnap, bool gridSnap)
        {
            if (!_placing || _roomRoot == null) return;
            EnsureGhost();
            if (_ghost == null) return;
            bool snapToSurface = surfaceSnap && hasSurface;
            Vector3 position = snapToSurface
                ? surfacePoint : ray.GetPoint(0.50f);
            Vector3 up = snapToSurface && !uprightSnap &&
                surfaceNormal.sqrMagnitude > 1e-8f
                    ? surfaceNormal.normalized : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(ray.direction, up);
            if (forward.sqrMagnitude < 1e-8f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 1e-8f)
                forward = Vector3.ProjectOnPlane(Vector3.right, up);
            Quaternion rotation = Quaternion.LookRotation(forward.normalized,
                up);
            Vector3 local = _roomRoot.InverseTransformPoint(position);
            if (gridSnap)
                local = new Vector3(Snap(local.x, 0.05f),
                    Snap(local.y, 0.05f), Snap(local.z, 0.05f));
            _ghost.transform.SetLocalPositionAndRotation(local,
                Quaternion.Inverse(_roomRoot.rotation) * rotation);
        }

        internal bool PlaceSelected()
        {
            if (!_placing || _ghost == null || _document == null) return false;
            var instance = new MerkabaDesignInstance
            {
                instanceId = _document.AllocateInstanceId(),
                assetId = _selectedAssetId,
                position = _ghost.transform.localPosition,
                rotation = _ghost.transform.localRotation,
                scale = _ghost.transform.localScale,
                visible = true,
                locked = false
            };
            _document.instances.Add(instance);
            if (!TryCreateVisual(instance))
            {
                _document.instances.Remove(instance);
                return false;
            }
            _selectedInstanceId = instance.instanceId;
            SetPlacementEnabled(false);
            RefreshSelectionVisual();
            MarkChanged();
            return true;
        }

        internal bool SelectInstance(int instanceId)
        {
            if (_document?.instances?.Exists(instance => instance != null &&
                    instance.instanceId == instanceId) != true)
                return false;
            _selectedInstanceId = instanceId;
            SetPlacementEnabled(false);
            RefreshSelectionVisual();
            return true;
        }

        internal bool SelectInstance(Ray ray)
        {
            float nearest = float.PositiveInfinity;
            int selected = 0;
            foreach ((int id, InstanceVisual visual) in _visuals)
            {
                if (!visual.Instance.visible || visual.Collider == null ||
                    !visual.Collider.Raycast(ray, out RaycastHit hit, 1000f) ||
                    hit.distance >= nearest) continue;
                nearest = hit.distance;
                selected = id;
            }
            _selectedInstanceId = selected;
            SetPlacementEnabled(false);
            RefreshSelectionVisual();
            return selected != 0;
        }

        internal bool DuplicateSelected()
        {
            MerkabaDesignInstance source = SelectedInstance;
            if (source == null || _document == null) return false;
            var copy = new MerkabaDesignInstance
            {
                instanceId = _document.AllocateInstanceId(),
                assetId = source.assetId,
                position = source.position + Vector3.right * 0.10f,
                rotation = source.rotation,
                scale = source.scale,
                visible = source.visible,
                locked = false
            };
            _document.instances.Add(copy);
            if (!TryCreateVisual(copy))
            {
                _document.instances.Remove(copy);
                return false;
            }
            _selectedInstanceId = copy.instanceId;
            RefreshSelectionVisual();
            MarkChanged();
            return true;
        }

        internal bool DeleteSelected()
        {
            MerkabaDesignInstance instance = SelectedInstance;
            if (instance == null || _document == null) return false;
            _document.instances.Remove(instance);
            if (_visuals.Remove(instance.instanceId, out InstanceVisual visual))
                DestroyObject(visual.Root);
            _selectedInstanceId = 0;
            EndGrab(false);
            RefreshSelectionVisual();
            MarkChanged();
            return true;
        }

        internal bool ToggleSelectedVisible()
        {
            MerkabaDesignInstance instance = SelectedInstance;
            if (instance == null) return false;
            instance.visible = !instance.visible;
            if (_visuals.TryGetValue(instance.instanceId,
                    out InstanceVisual visual))
                visual.Root.SetActive(instance.visible);
            MarkChanged();
            return true;
        }

        internal bool ToggleSelectedLocked()
        {
            MerkabaDesignInstance instance = SelectedInstance;
            if (instance == null) return false;
            instance.locked = !instance.locked;
            if (instance.locked) EndGrab(true);
            MarkChanged();
            return true;
        }

        internal bool ContinueOneHandGrab(Vector3 controllerPosition,
            Quaternion controllerRotation)
        {
            if (!TryGetEditableVisual(out InstanceVisual visual)) return false;
            if (_grabMode != GrabMode.OneHand)
            {
                _oneHandGrab = new OneHandGrab(controllerPosition,
                    controllerRotation, visual.Root.transform.position,
                    visual.Root.transform.rotation,
                    visual.Root.transform.localScale);
                _grabMode = GrabMode.OneHand;
            }
            ApplyOneHandTransform(visual.Root.transform, _oneHandGrab,
                controllerPosition, controllerRotation);
            CopyTransformToInstance(visual);
            return true;
        }

        internal bool ContinueTwoHandGrab(Vector3 leftPosition,
            Quaternion leftRotation, Vector3 rightPosition,
            Quaternion rightRotation)
        {
            if (!TryGetEditableVisual(out InstanceVisual visual) ||
                !TryBuildTwoHandFrame(leftPosition, leftRotation,
                    rightPosition, rightRotation, out Vector3 midpoint,
                    out Quaternion frame, out float separation)) return false;
            if (_grabMode != GrabMode.TwoHand)
            {
                _twoHandGrab = new TwoHandGrab(midpoint, frame, separation,
                    visual.Root.transform.position,
                    visual.Root.transform.rotation,
                    visual.Root.transform.localScale);
                _grabMode = GrabMode.TwoHand;
            }
            ApplyTwoHandTransform(visual.Root.transform, _twoHandGrab,
                midpoint, frame, separation);
            CopyTransformToInstance(visual);
            return true;
        }

        internal void EndGrab(bool changed)
        {
            if (_grabMode == GrabMode.None) return;
            _grabMode = GrabMode.None;
            if (changed) MarkChanged();
        }

        internal void Refresh()
        {
            Directory.CreateDirectory(_root);
            _assets.Clear();
            string[] metadataPaths = Directory.GetFiles(_root, "*.json",
                SearchOption.TopDirectoryOnly);
            Array.Sort(metadataPaths, StringComparer.Ordinal);
            foreach (string metadataPath in metadataPaths)
            {
                try
                {
                    MerkabaDesignAsset asset = ReadMetadata(metadataPath);
                    if (!File.Exists(AssetPath(asset.id)))
                        throw new FileNotFoundException(
                            "Design GLB bytes are missing.", AssetPath(asset.id));
                    _assets.Add(asset);
                }
                catch (Exception exception)
                {
                    Logger.Warning($"Ignoring invalid design asset metadata " +
                        $"'{Path.GetFileName(metadataPath)}': " +
                        exception.Message);
                }
            }
            _assets.Sort((left, right) =>
            {
                int name = string.Compare(left.displayName, right.displayName,
                    StringComparison.OrdinalIgnoreCase);
                return name != 0 ? name : string.CompareOrdinal(left.id,
                    right.id);
            });
        }

        internal MerkabaDesignAsset Import(string sourcePath)
        {
            MerkabaDesignAsset imported = ImportFile(sourcePath);
            Refresh();
            return _assets.Find(value => value.id == imported.id) ?? imported;
        }

        /// <summary>
        /// Performs only file IO/validation so callers may run it off-thread.
        /// Refresh and all Unity-object work remain on the Unity thread.
        /// </summary>
        internal MerkabaDesignAsset ImportFile(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
                throw new FileNotFoundException("Imported GLB was not found.",
                    sourcePath);
            Directory.CreateDirectory(_root);
            string staging = Path.Combine(_root, ".import-" +
                Guid.NewGuid().ToString("N") + ".glb.tmp");
            try
            {
                string id = CopyAndHash(sourcePath, staging);
                MerkabaArtifactViewer.ParsedGlb parsed;
                using (var input = new FileStream(staging, FileMode.Open,
                           FileAccess.Read, FileShare.Read, CopyBufferBytes,
                           FileOptions.SequentialScan))
                    parsed = MerkabaArtifactViewer.ParseGlbForPreview(input,
                        input.Length);

                string destination = AssetPath(id);
                if (File.Exists(destination))
                    File.Delete(staging);
                else
                    MerkabaFilePublishing.Publish(staging, destination);

                string metadataPath = MetadataPath(id);
                MerkabaDesignAsset asset = File.Exists(metadataPath)
                    ? ReadMetadata(metadataPath)
                    : CreateMetadata(id, sourcePath, parsed);
                if (!File.Exists(metadataPath)) WriteMetadata(asset);
                return asset;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }

        internal MerkabaArtifactViewer.ParsedGlb Decode(string assetId)
        {
            string path = AssetPath(assetId);
            using var input = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, CopyBufferBytes,
                FileOptions.SequentialScan);
            return MerkabaArtifactViewer.ParseGlbForPreview(input,
                input.Length);
        }

        internal static bool TryBuildTwoHandFrame(Vector3 leftPosition,
            Quaternion leftRotation, Vector3 rightPosition,
            Quaternion rightRotation, out Vector3 midpoint,
            out Quaternion frame, out float separation)
        {
            midpoint = (leftPosition + rightPosition) * 0.5f;
            Vector3 x = rightPosition - leftPosition;
            separation = x.magnitude;
            if (separation < 0.03f)
            {
                frame = default;
                return false;
            }
            x /= separation;
            Vector3 up = Vector3.ProjectOnPlane(
                leftRotation * Vector3.up + rightRotation * Vector3.up, x);
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.ProjectOnPlane(leftRotation * Vector3.forward +
                    rightRotation * Vector3.forward, x);
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.ProjectOnPlane(Vector3.up, x);
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.ProjectOnPlane(Vector3.forward, x);
            up.Normalize();
            Vector3 forward = Vector3.Cross(x, up).normalized;
            up = Vector3.Cross(forward, x).normalized;
            frame = Quaternion.LookRotation(forward, up);
            return true;
        }

        internal static void ApplyOneHandTransform(Transform target,
            OneHandGrab start, Vector3 controllerPosition,
            Quaternion controllerRotation)
        {
            Quaternion deltaRotation = controllerRotation *
                Quaternion.Inverse(start.ControllerRotation);
            target.SetPositionAndRotation(controllerPosition + deltaRotation *
                (start.TargetPosition - start.ControllerPosition),
                deltaRotation * start.TargetRotation);
            target.localScale = start.TargetScale;
        }

        internal static void ApplyTwoHandTransform(Transform target,
            TwoHandGrab start, Vector3 midpoint, Quaternion frame,
            float separation)
        {
            Quaternion deltaRotation = frame *
                Quaternion.Inverse(start.HandFrame);
            float scaleRatio = separation / start.Separation;
            Vector3 scale = start.TargetScale * scaleRatio;
            float uniform = Mathf.Clamp(scale.x, 0.002f, 10f);
            float clampedRatio = uniform /
                Mathf.Max(1e-6f, start.TargetScale.x);
            target.SetPositionAndRotation(midpoint + deltaRotation *
                ((start.TargetPosition - start.Midpoint) * clampedRatio),
                deltaRotation * start.TargetRotation);
            target.localScale = Vector3.one * uniform;
        }

        internal MerkabaDesignAsset Find(string assetId) =>
            _assets.Find(asset => string.Equals(asset.id, assetId,
                StringComparison.Ordinal));

        internal string AssetPath(string assetId)
        {
            ValidateId(assetId);
            return Path.Combine(_root, assetId + ".glb");
        }

        private string MetadataPath(string assetId)
        {
            ValidateId(assetId);
            return Path.Combine(_root, assetId + ".json");
        }

        private static string CopyAndHash(string sourcePath, string staging)
        {
            using var hash = SHA256.Create();
            using var input = new FileStream(sourcePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, CopyBufferBytes,
                FileOptions.SequentialScan);
            if (input.Length == 0L)
                throw new InvalidDataException("Imported GLB is empty.");
            using (var output = new FileStream(staging, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, CopyBufferBytes,
                       FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                output.Flush(true);
            }
            return Hex(hash.Hash);
        }

        private bool TryCreateVisual(MerkabaDesignInstance instance)
        {
            if (instance == null || _objectRoot == null ||
                Find(instance.assetId) == null) return false;
            try
            {
                Mesh mesh = MeshFor(instance.assetId);
                MerkabaDesignAsset asset = Find(instance.assetId);
                var root = new GameObject("Design Object " +
                    instance.instanceId);
                root.transform.SetParent(_objectRoot, false);
                root.transform.SetLocalPositionAndRotation(instance.position,
                    instance.rotation);
                root.transform.localScale = SanitizedScale(instance.scale);
                var geometry = new GameObject(asset.displayName);
                geometry.transform.SetParent(root.transform, false);
                geometry.transform.localPosition = -Pivot(asset.Bounds);
                var filter = geometry.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = geometry.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _objectMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var collider = geometry.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                root.SetActive(instance.visible);
                _visuals[instance.instanceId] = new InstanceVisual(instance,
                    root, renderer, collider);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"Could not restore design object " +
                    $"#{instance.instanceId}: {exception.Message}");
                return false;
            }
        }

        private Mesh MeshFor(string assetId)
        {
            if (_meshes.TryGetValue(assetId, out Mesh cached)) return cached;
            MerkabaArtifactViewer.ParsedGlb parsed = Decode(assetId);
            var mesh = new Mesh
            {
                name = "Design Asset " + assetId.Substring(0, 8),
                indexFormat = IndexFormat.UInt32,
                vertices = parsed.Positions,
                normals = parsed.Normals,
                colors32 = parsed.Colors,
                triangles = parsed.Indices
            };
            mesh.RecalculateBounds();
            _meshes.Add(assetId, mesh);
            return mesh;
        }

        private void EnsureGhost()
        {
            if (!_placing || string.IsNullOrEmpty(_selectedAssetId) ||
                _objectRoot == null) return;
            if (_ghost != null && _ghostAssetId == _selectedAssetId)
            {
                _ghost.SetActive(true);
                return;
            }
            if (_ghost != null) DestroyObject(_ghost);
            MerkabaDesignAsset asset = Find(_selectedAssetId);
            if (asset == null) return;
            Mesh mesh = MeshFor(asset.id);
            _ghost = new GameObject("Design Object Placement");
            _ghost.transform.SetParent(_objectRoot, false);
            var geometry = new GameObject(asset.displayName + " Ghost");
            geometry.transform.SetParent(_ghost.transform, false);
            geometry.transform.localPosition = -Pivot(asset.Bounds);
            var filter = geometry.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = geometry.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _ghostMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _ghostAssetId = asset.id;
        }

        private bool TryGetEditableVisual(out InstanceVisual visual)
        {
            if (_selectedInstanceId != 0 && _visuals.TryGetValue(
                    _selectedInstanceId, out visual) &&
                visual.Instance.visible && !visual.Instance.locked)
                return true;
            visual = default;
            EndGrab(false);
            return false;
        }

        private static void CopyTransformToInstance(InstanceVisual visual)
        {
            visual.Instance.position = visual.Root.transform.localPosition;
            visual.Instance.rotation = visual.Root.transform.localRotation;
            visual.Instance.scale = visual.Root.transform.localScale;
        }

        private void RefreshSelectionVisual()
        {
            var properties = new MaterialPropertyBlock();
            foreach ((int id, InstanceVisual visual) in _visuals)
            {
                properties.Clear();
                properties.SetColor(BaseColorId, id == _selectedInstanceId
                    ? new Color(1f, 0.86f, 0.48f, 1f)
                    : Color.white);
                visual.Renderer.SetPropertyBlock(properties);
            }
        }

        private void MarkChanged() => _changed?.Invoke();

        private static Vector3 Pivot(Bounds bounds) => new(bounds.center.x,
            bounds.min.y, bounds.center.z);

        private static Vector3 SanitizedScale(Vector3 scale) => new(
            Mathf.Max(0.002f, Mathf.Abs(scale.x)),
            Mathf.Max(0.002f, Mathf.Abs(scale.y)),
            Mathf.Max(0.002f, Mathf.Abs(scale.z)));

        private static float Snap(float value, float step) =>
            Mathf.Round(value / step) * step;

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        private MerkabaDesignAsset CreateMetadata(string id,
            string sourcePath, MerkabaArtifactViewer.ParsedGlb parsed)
        {
            if (parsed.Positions.Length == 0)
                throw new InvalidDataException(
                    "Imported GLB contains no design geometry.");
            Bounds bounds = new(parsed.Positions[0], Vector3.zero);
            for (int index = 1; index < parsed.Positions.Length; index++)
                bounds.Encapsulate(parsed.Positions[index]);
            string name = Path.GetFileNameWithoutExtension(sourcePath)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Imported object";
            if (name.Length > 80) name = name.Substring(0, 80);
            return new MerkabaDesignAsset
            {
                formatVersion = FormatVersion,
                id = id,
                displayName = name,
                boundsCenter = bounds.center,
                boundsSize = bounds.size,
                importedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture)
            };
        }

        private void WriteMetadata(MerkabaDesignAsset asset)
        {
            string destination = MetadataPath(asset.id);
            string temporary = destination + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(
                JsonUtility.ToJson(asset, true) + "\n");
            using (var stream = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 16 * 1024,
                       FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            MerkabaFilePublishing.Publish(temporary, destination);
        }

        private static MerkabaDesignAsset ReadMetadata(string path)
        {
            MerkabaDesignAsset asset = JsonUtility.FromJson<
                MerkabaDesignAsset>(File.ReadAllText(path, Encoding.UTF8));
            if (asset == null || asset.formatVersion != FormatVersion)
                throw new InvalidDataException(
                    "Design asset metadata has an unsupported format.");
            ValidateId(asset.id);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path),
                    asset.id, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Design asset metadata does not match its filename.");
            if (string.IsNullOrWhiteSpace(asset.displayName))
                asset.displayName = "Imported object";
            return asset;
        }

        private static void ValidateId(string value)
        {
            if (value == null || value.Length != 64)
                throw new InvalidDataException("Invalid design asset ID.");
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f'))
                    throw new InvalidDataException("Invalid design asset ID.");
            }
        }

        private static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        internal readonly struct OneHandGrab
        {
            internal readonly Vector3 ControllerPosition;
            internal readonly Quaternion ControllerRotation;
            internal readonly Vector3 TargetPosition;
            internal readonly Quaternion TargetRotation;
            internal readonly Vector3 TargetScale;

            internal OneHandGrab(Vector3 controllerPosition,
                Quaternion controllerRotation, Vector3 targetPosition,
                Quaternion targetRotation, Vector3 targetScale)
            {
                ControllerPosition = controllerPosition;
                ControllerRotation = controllerRotation;
                TargetPosition = targetPosition;
                TargetRotation = targetRotation;
                TargetScale = targetScale;
            }
        }

        internal readonly struct TwoHandGrab
        {
            internal readonly Vector3 Midpoint;
            internal readonly Quaternion HandFrame;
            internal readonly float Separation;
            internal readonly Vector3 TargetPosition;
            internal readonly Quaternion TargetRotation;
            internal readonly Vector3 TargetScale;

            internal TwoHandGrab(Vector3 midpoint, Quaternion handFrame,
                float separation, Vector3 targetPosition,
                Quaternion targetRotation, Vector3 targetScale)
            {
                Midpoint = midpoint;
                HandFrame = handFrame;
                Separation = separation;
                TargetPosition = targetPosition;
                TargetRotation = targetRotation;
                TargetScale = targetScale;
            }
        }

        private readonly struct InstanceVisual
        {
            internal readonly MerkabaDesignInstance Instance;
            internal readonly GameObject Root;
            internal readonly MeshRenderer Renderer;
            internal readonly MeshCollider Collider;

            internal InstanceVisual(MerkabaDesignInstance instance,
                GameObject root, MeshRenderer renderer,
                MeshCollider collider)
            {
                Instance = instance;
                Root = root;
                Renderer = renderer;
                Collider = collider;
            }
        }

        private enum GrabMode
        {
            None,
            OneHand,
            TwoHand
        }
    }
}
