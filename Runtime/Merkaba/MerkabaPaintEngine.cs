using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Disposable renderer/editor for session design strokes. It owns no M8
    /// state and updates only the active or explicitly edited stroke mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaPaintEngine : MonoBehaviour
    {
        private const float MinimumRayDistance = 0.01f;
        private const int RoundSegments = 8;
        private const int HistoryCapacity = 24;
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int SourceBlendId =
            Shader.PropertyToID("_SrcBlend");
        private static readonly int DestinationBlendId =
            Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaDitherId =
            Shader.PropertyToID("_AlphaDither");

        private readonly Dictionary<int, StrokeVisual> _visuals = new();
        private readonly List<string> _undoHistory = new();
        private readonly List<string> _redoHistory = new();
        private Transform _roomRoot;
        private Transform _paintRoot;
        private Material _material;
        private MerkabaDesignDocument _document;
        private MerkabaDesignStroke _activeStroke;
        private string _path;
        private bool _dirty;
        private float _sprayAccumulator;
        private uint _sprayOrdinal;
        private string _pendingHistory;

        internal event Action Changed;
        internal bool IsOpen => _document != null;
        internal bool IsDirty => _dirty;
        internal bool HasActiveStroke => _activeStroke != null;
        internal bool CanUndo => _undoHistory.Count > 0;
        internal bool CanRedo => _redoHistory.Count > 0;
        internal int StrokeCount => _document?.strokes?.Count ?? 0;
        internal MerkabaDesignDocument Document => _document;

        private void OnDestroy() => Close();

        internal void Open(Transform roomRoot, Shader shader, string path)
        {
            Close();
            if (roomRoot == null)
                throw new ArgumentNullException(nameof(roomRoot));
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(
                    "An active session design path is required.", nameof(path));
            _roomRoot = roomRoot;
            _path = path;
            _document = MerkabaDesignDocument.Load(path);
            var root = new GameObject("Merkaba Session Paint");
            _paintRoot = root.transform;
            _paintRoot.SetParent(_roomRoot, false);
            _material = new Material(shader)
            {
                name = "Merkaba Session Paint",
                hideFlags = HideFlags.DontSave
            };
            ConfigureTransparentMaterial(_material);
            foreach (MerkabaDesignStroke stroke in _document.strokes)
                if (stroke?.samples != null && stroke.samples.Count > 0)
                    RebuildVisual(stroke);
            _undoHistory.Clear();
            _redoHistory.Clear();
            _pendingHistory = null;
            _dirty = false;
        }

        internal void Close()
        {
            if (_activeStroke != null)
            {
                RemoveStroke(_activeStroke);
                _activeStroke = null;
            }
            _pendingHistory = null;
            foreach (StrokeVisual visual in _visuals.Values)
                DestroyVisual(visual);
            _visuals.Clear();
            if (_paintRoot != null) DestroyObject(_paintRoot.gameObject);
            if (_material != null) DestroyObject(_material);
            _paintRoot = null;
            _material = null;
            _document = null;
            _roomRoot = null;
            _path = null;
            _undoHistory.Clear();
            _redoHistory.Clear();
            _pendingHistory = null;
            _dirty = false;
        }

        internal bool Save()
        {
            if (!_dirty) return true;
            try
            {
                _document?.Save(_path);
                _dirty = false;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Could not save session design: " + exception);
                return false;
            }
        }

        internal void BeginStroke(MerkabaDesignTool tool,
            MerkabaPaintSettings settings)
        {
            CancelStroke();
            if (_document == null || _paintRoot == null) return;
            BeginDocumentChange();
            int id = _document.AllocateStrokeId();
            Color color = ApplySaturation(settings.Color,
                settings.Saturation);
            color.a = settings.Opacity;
            _activeStroke = new MerkabaDesignStroke
            {
                id = id,
                tool = tool,
                color = color,
                opacity = settings.Opacity,
                flow = settings.Flow,
                hardness = settings.Hardness,
                saturation = settings.Saturation,
                radius = WorldToRoomRadius(settings.Radius),
                shape = settings.Shape,
                seed = Hash((uint)id ^ 0x9e3779b9u),
                samples = new List<MerkabaDesignSample>()
            };
            _document.strokes.Add(_activeStroke);
            _sprayAccumulator = 0f;
            _sprayOrdinal = 0u;
        }

        internal void AddSample(Vector3 worldPosition, Vector3 worldNormal,
            bool hasNormal, float radius = -1f)
        {
            if (_activeStroke == null) return;
            int first = _activeStroke.samples.Count;
            AppendSample(new PaintInputSample(worldPosition, worldNormal,
                hasNormal, radius));
            AppendVisualSamples(_activeStroke, first);
        }

        internal int AddSamples(IReadOnlyList<PaintInputSample> samples)
        {
            if (_activeStroke == null || samples == null) return 0;
            int before = _activeStroke.samples.Count;
            for (int index = 0; index < samples.Count; index++)
                AppendSample(samples[index]);
            if (_activeStroke.samples.Count != before)
                AppendVisualSamples(_activeStroke, before);
            return _activeStroke.samples.Count - before;
        }

        internal void SetLine(Vector3 worldStart, Vector3 worldEnd,
            Vector3 worldNormal, bool hasNormal)
        {
            if (_activeStroke == null ||
                _activeStroke.tool != MerkabaDesignTool.Line) return;
            float distance = Vector3.Distance(worldStart, worldEnd);
            float spacing = Mathf.Max(0.002f,
                RoomToWorldRadius(_activeStroke.radius) * 0.5f);
            int count = Mathf.Max(2, Mathf.CeilToInt(distance / spacing) + 1);
            _activeStroke.samples.Clear();
            for (int index = 0; index < count; index++)
            {
                float t = count > 1 ? index / (float)(count - 1) : 0f;
                _activeStroke.samples.Add(new MerkabaDesignSample
                {
                    position = WorldToRoomPoint(Vector3.Lerp(worldStart,
                        worldEnd, t)),
                    normal = hasNormal
                        ? WorldToRoomDirection(worldNormal).normalized
                        : Vector3.zero,
                    hasNormal = hasNormal,
                    radius = _activeStroke.radius
                });
            }
            RebuildVisual(_activeStroke);
        }

        internal int AddSpray(Vector3 worldCenter, Vector3 worldAxis,
            float deltaTime, float density, float scatter)
        {
            if (_activeStroke == null ||
                _activeStroke.tool != MerkabaDesignTool.Spray) return 0;
            _sprayAccumulator += Mathf.Max(0f, deltaTime) *
                Mathf.Max(1f, density) * Mathf.Max(0.05f,
                    _activeStroke.flow);
            int count = Mathf.Min(64, Mathf.FloorToInt(_sprayAccumulator));
            if (count <= 0) return 0;
            _sprayAccumulator -= count;
            int first = _activeStroke.samples.Count;
            BuildBasis(worldAxis, out Vector3 tangent0, out Vector3 tangent1);
            float radius = Mathf.Max(0f, scatter);
            for (int index = 0; index < count; index++)
            {
                uint ordinal = _sprayOrdinal++;
                Vector3 offset = DeterministicSprayOffset(_activeStroke.seed,
                    ordinal, tangent0, tangent1, worldAxis, radius,
                    _activeStroke.shape);
                AppendSample(new PaintInputSample(worldCenter + offset,
                    Vector3.zero, false));
            }
            AppendVisualSamples(_activeStroke, first);
            return count;
        }

        internal bool CommitStroke()
        {
            if (_activeStroke == null) return false;
            int minimum = _activeStroke.tool == MerkabaDesignTool.Line ? 2 : 1;
            bool valid = _activeStroke.samples.Count >= minimum;
            if (valid && _activeStroke.tool == MerkabaDesignTool.Line)
                valid = (_activeStroke.samples[^1].position -
                    _activeStroke.samples[0].position).sqrMagnitude >= 1e-8f;
            if (!valid)
            {
                RemoveStroke(_activeStroke);
                _activeStroke = null;
                RollbackDocumentChange();
                return false;
            }
            _activeStroke = null;
            CommitDocumentChange();
            return true;
        }

        internal void CancelStroke()
        {
            if (_activeStroke == null) return;
            RemoveStroke(_activeStroke);
            _activeStroke = null;
            _sprayAccumulator = 0f;
            RollbackDocumentChange();
        }

        internal bool TrySample(Ray worldRay, out PaintHit hit)
        {
            hit = default;
            if (_document?.strokes == null || _paintRoot == null) return false;
            float nearest = float.PositiveInfinity;
            bool found = false;
            foreach (MerkabaDesignStroke stroke in _document.strokes)
            foreach (MerkabaDesignSample sample in stroke.samples)
            {
                Vector3 point = RoomToWorldPoint(sample.position);
                float along = Vector3.Dot(point - worldRay.origin,
                    worldRay.direction);
                if (along < MinimumRayDistance || along >= nearest) continue;
                float pickRadius = RoomToWorldRadius(Mathf.Max(sample.radius,
                    stroke.radius)) * 1.5f;
                if ((worldRay.GetPoint(along) - point).sqrMagnitude >
                    pickRadius * pickRadius) continue;
                nearest = along;
                hit = new PaintHit(point, StrokeColor(stroke), stroke.id,
                    along);
                found = true;
            }
            return found;
        }

        internal int EraseSphere(Vector3 worldCenter, float worldRadius)
        {
            if (_document?.strokes == null || _paintRoot == null) return 0;
            BeginDocumentChange();
            Vector3 center = WorldToRoomPoint(worldCenter);
            float radius = WorldToRoomRadius(Mathf.Max(0.001f, worldRadius));
            int removed = 0;
            for (int strokeIndex = _document.strokes.Count - 1;
                 strokeIndex >= 0; strokeIndex--)
            {
                MerkabaDesignStroke stroke = _document.strokes[strokeIndex];
                if (stroke == _activeStroke) continue;
                List<List<MerkabaDesignSample>> runs = SplitOutsideSphere(
                    stroke, center, radius, out int removedFromStroke);
                if (removedFromStroke == 0) continue;
                removed += removedFromStroke;
                _document.strokes.RemoveAt(strokeIndex);
                DestroyVisualFor(stroke.id);
                int insertionIndex = strokeIndex;
                for (int runIndex = 0; runIndex < runs.Count; runIndex++)
                {
                    List<MerkabaDesignSample> run = runs[runIndex];
                    int minimum = stroke.tool == MerkabaDesignTool.Line ? 2 : 1;
                    if (run.Count < minimum) continue;
                    int id = runIndex == 0 ? stroke.id :
                        _document.AllocateStrokeId();
                    MerkabaDesignStroke replacement =
                        stroke.CopyWithIdAndSamples(id, run);
                    _document.strokes.Insert(insertionIndex++, replacement);
                    RebuildVisual(replacement);
                }
            }
            if (removed > 0) CommitDocumentChange();
            else DiscardDocumentChange();
            return removed;
        }

        internal void BeginDocumentChange()
        {
            if (_document == null || _pendingHistory != null) return;
            _pendingHistory = _document.CaptureSnapshot();
        }

        internal void CommitDocumentChange()
        {
            if (_document == null) return;
            if (_pendingHistory == null)
            {
                MarkChanged();
                return;
            }
            string before = _pendingHistory;
            _pendingHistory = null;
            if (string.Equals(before, _document.CaptureSnapshot(),
                    StringComparison.Ordinal)) return;
            PushHistory(_undoHistory, before);
            _redoHistory.Clear();
            MarkChanged();
        }

        internal void RollbackDocumentChange()
        {
            if (_document == null || _pendingHistory == null) return;
            string before = _pendingHistory;
            _pendingHistory = null;
            _document.RestoreSnapshot(before);
            RebuildAllVisuals();
        }

        internal void DiscardDocumentChange() => _pendingHistory = null;

        internal bool Undo()
        {
            CancelStroke();
            return RestoreHistory(_undoHistory, _redoHistory);
        }

        internal bool Redo()
        {
            CancelStroke();
            return RestoreHistory(_redoHistory, _undoHistory);
        }

        internal bool ImportLegacy(MerkabaDesignTool tool, Color color,
            float radius, IReadOnlyList<Vector3> worldPoints)
        {
            if (worldPoints == null || worldPoints.Count == 0 ||
                _document == null) return false;
            BeginStroke(tool, new MerkabaPaintSettings(color, color.a, 1f,
                0.8f, 1f, radius, MerkabaBrushShape.Round));
            foreach (Vector3 point in worldPoints)
                AppendSample(new PaintInputSample(point, Vector3.zero, false,
                    radius));
            RebuildVisual(_activeStroke);
            return CommitStroke();
        }

        internal static Vector3 SpatialBrushPoint(Ray ray) =>
            ray.GetPoint(0.20f);

        internal static float SurfaceSampleSpacing(float radius) =>
            Mathf.Max(0.001f, Mathf.Max(0.001f, radius) * 0.20f);

        internal static Vector3 DeterministicSprayOffset(uint seed,
            uint ordinal, Vector3 tangent0, Vector3 tangent1, Vector3 axis,
            float scatter, MerkabaBrushShape shape)
        {
            float a = Unit(Hash(seed ^ ordinal * 0x85ebca6bu));
            float b = Unit(Hash(seed ^ ordinal * 0xc2b2ae35u));
            float c = Unit(Hash(seed ^ ordinal * 0x27d4eb2fu));
            Vector3 offset;
            if (shape == MerkabaBrushShape.Square)
                offset = tangent0 * ((a * 2f - 1f) * scatter) +
                    tangent1 * ((b * 2f - 1f) * scatter);
            else
            {
                float radial = Mathf.Sqrt(a) * scatter;
                float angle = b * Mathf.PI * 2f;
                offset = tangent0 * (Mathf.Cos(angle) * radial) +
                    tangent1 * (Mathf.Sin(angle) * radial);
            }
            Vector3 normal = axis.sqrMagnitude > 1e-10f
                ? axis.normalized : Vector3.forward;
            return offset + normal * ((c - 0.5f) * scatter * 0.3f);
        }

        private void AppendSample(PaintInputSample sample)
        {
            if (_activeStroke == null) return;
            _activeStroke.samples.Add(new MerkabaDesignSample
            {
                position = WorldToRoomPoint(sample.WorldPosition),
                normal = sample.HasNormal
                    ? WorldToRoomDirection(sample.WorldNormal).normalized
                    : Vector3.zero,
                hasNormal = sample.HasNormal,
                radius = sample.Radius > 0f
                    ? WorldToRoomRadius(sample.Radius)
                    : _activeStroke.radius
            });
        }

        private List<List<MerkabaDesignSample>> SplitOutsideSphere(
            MerkabaDesignStroke stroke, Vector3 center, float eraserRadius,
            out int removed)
        {
            removed = 0;
            var runs = new List<List<MerkabaDesignSample>>();
            List<MerkabaDesignSample> current = null;
            bool independent = stroke.tool == MerkabaDesignTool.Spray;
            foreach (MerkabaDesignSample sample in stroke.samples)
            {
                float radius = eraserRadius + Mathf.Max(sample.radius,
                    stroke.radius) * 0.5f;
                if ((sample.position - center).sqrMagnitude <= radius * radius)
                {
                    removed++;
                    if (!independent) current = null;
                    continue;
                }
                if (independent)
                {
                    current ??= new List<MerkabaDesignSample>();
                    if (runs.Count == 0) runs.Add(current);
                }
                else if (current == null)
                {
                    current = new List<MerkabaDesignSample>();
                    runs.Add(current);
                }
                current.Add(sample);
            }
            return runs;
        }

        private void RemoveStroke(MerkabaDesignStroke stroke)
        {
            _document?.strokes?.Remove(stroke);
            DestroyVisualFor(stroke.id);
        }

        private bool RestoreHistory(List<string> source, List<string> target)
        {
            if (_document == null || source.Count == 0) return false;
            string current = _document.CaptureSnapshot();
            string replacement = source[^1];
            source.RemoveAt(source.Count - 1);
            PushHistory(target, current);
            _pendingHistory = null;
            _document.RestoreSnapshot(replacement);
            RebuildAllVisuals();
            MarkChanged();
            return true;
        }

        private static void PushHistory(List<string> history, string snapshot)
        {
            if (history.Count == HistoryCapacity) history.RemoveAt(0);
            history.Add(snapshot);
        }

        private void RebuildAllVisuals()
        {
            foreach (StrokeVisual visual in _visuals.Values)
                DestroyVisual(visual);
            _visuals.Clear();
            if (_document?.strokes == null) return;
            foreach (MerkabaDesignStroke stroke in _document.strokes)
                if (stroke?.samples != null && stroke.samples.Count > 0)
                    RebuildVisual(stroke);
        }

        private void RebuildVisual(MerkabaDesignStroke stroke)
        {
            if (_paintRoot == null || _material == null) return;
            if (!_visuals.TryGetValue(stroke.id, out StrokeVisual visual))
            {
                var gameObject = new GameObject("Design Stroke " + stroke.id);
                gameObject.transform.SetParent(_paintRoot, false);
                var filter = gameObject.AddComponent<MeshFilter>();
                var renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var mesh = new Mesh
                {
                    name = "Design Stroke " + stroke.id,
                    indexFormat = IndexFormat.UInt32
                };
                mesh.MarkDynamic();
                filter.sharedMesh = mesh;
                visual = new StrokeVisual(gameObject, mesh);
                _visuals.Add(stroke.id, visual);
            }
            BuildStrokeMesh(stroke, visual);
        }

        private static void BuildStrokeMesh(MerkabaDesignStroke stroke,
            StrokeVisual visual)
        {
            visual.Vertices.Clear();
            visual.Colors.Clear();
            visual.Indices.Clear();
            Color color = StrokeColor(stroke);
            if (stroke.tool == MerkabaDesignTool.Line)
            {
                for (int index = 1; index < stroke.samples.Count; index++)
                    AppendTube(stroke.samples[index - 1].position,
                        stroke.samples[index].position,
                        Mathf.Max(stroke.samples[index].radius, 0.001f), color,
                        visual.Vertices, visual.Colors, visual.Indices);
            }
            else foreach (MerkabaDesignSample sample in stroke.samples)
                AppendSampleGeometry(stroke, sample, color, visual);
            visual.SampleCount = stroke.samples.Count;
            UploadVisual(visual);
        }

        private void AppendVisualSamples(MerkabaDesignStroke stroke,
            int firstSample)
        {
            if (!_visuals.TryGetValue(stroke.id, out StrokeVisual visual))
            {
                RebuildVisual(stroke);
                return;
            }
            if (stroke.tool == MerkabaDesignTool.Line ||
                firstSample != visual.SampleCount)
            {
                BuildStrokeMesh(stroke, visual);
                return;
            }
            Color color = StrokeColor(stroke);
            for (int index = firstSample; index < stroke.samples.Count; index++)
                AppendSampleGeometry(stroke, stroke.samples[index], color,
                    visual);
            visual.SampleCount = stroke.samples.Count;
            UploadVisual(visual);
        }

        private static void AppendSampleGeometry(MerkabaDesignStroke stroke,
            MerkabaDesignSample sample, Color color, StrokeVisual visual)
        {
            float radius = Mathf.Max(sample.radius, 0.001f);
            if (sample.hasNormal && stroke.tool !=
                MerkabaDesignTool.SpatialBrush && stroke.tool !=
                MerkabaDesignTool.Spray)
                AppendSurfaceDab(sample.position, sample.normal, radius,
                    stroke.hardness, stroke.shape, color, visual.Vertices,
                    visual.Colors, visual.Indices);
            else
                AppendVolumeDab(sample.position, radius, stroke.shape, color,
                    visual.Vertices, visual.Colors, visual.Indices);
        }

        private static void UploadVisual(StrokeVisual visual)
        {
            visual.Mesh.Clear(false);
            visual.Mesh.SetVertices(visual.Vertices);
            visual.Mesh.SetColors(visual.Colors);
            visual.Mesh.SetIndices(visual.Indices, MeshTopology.Triangles, 0,
                false);
            visual.Mesh.RecalculateBounds();
        }

        private static void AppendSurfaceDab(Vector3 center, Vector3 normal,
            float radius, float hardness, MerkabaBrushShape shape, Color color,
            List<Vector3> vertices, List<Color32> colors, List<int> indices)
        {
            BuildBasis(normal, out Vector3 tangent0, out Vector3 tangent1);
            int segments = shape == MerkabaBrushShape.Round ? RoundSegments : 4;
            float innerRadius = radius * Mathf.Lerp(0.08f, 0.98f,
                Mathf.Clamp01(hardness));
            int first = vertices.Count;
            vertices.Add(center);
            colors.Add(color);
            for (int ring = 0; ring < 2; ring++)
            {
                float ringRadius = ring == 0 ? innerRadius : radius;
                Color ringColor = ring == 0 ? color :
                    new Color(color.r, color.g, color.b, 0f);
                for (int index = 0; index < segments; index++)
                {
                    float angle = (index + (shape == MerkabaBrushShape.Square
                        ? 0.5f : 0f)) * Mathf.PI * 2f / segments;
                    vertices.Add(center + tangent0 * (Mathf.Cos(angle) *
                        ringRadius) + tangent1 * (Mathf.Sin(angle) *
                        ringRadius));
                    colors.Add(ringColor);
                }
            }
            for (int index = 0; index < segments; index++)
            {
                int next = (index + 1) % segments;
                indices.Add(first);
                indices.Add(first + 1 + index);
                indices.Add(first + 1 + next);
                int inner = first + 1 + index;
                int innerNext = first + 1 + next;
                int outer = first + 1 + segments + index;
                int outerNext = first + 1 + segments + next;
                indices.Add(inner);
                indices.Add(outer);
                indices.Add(outerNext);
                indices.Add(inner);
                indices.Add(outerNext);
                indices.Add(innerNext);
            }
        }

        private static void AppendVolumeDab(Vector3 center, float radius,
            MerkabaBrushShape shape, Color color, List<Vector3> vertices,
            List<Color32> colors, List<int> indices)
        {
            int first = vertices.Count;
            if (shape == MerkabaBrushShape.Square)
            {
                for (int z = -1; z <= 1; z += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int x = -1; x <= 1; x += 2)
                {
                    vertices.Add(center + new Vector3(x, y, z) * radius);
                    colors.Add(color);
                }
                int[] cube =
                {
                    0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6,
                    0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3,
                    0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5
                };
                foreach (int index in cube) indices.Add(first + index);
                return;
            }
            Vector3[] octa =
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down,
                Vector3.forward, Vector3.back
            };
            foreach (Vector3 direction in octa)
            {
                vertices.Add(center + direction * radius);
                colors.Add(color);
            }
            int[] triangles =
            {
                2, 0, 4, 2, 4, 1, 2, 1, 5, 2, 5, 0,
                3, 4, 0, 3, 1, 4, 3, 5, 1, 3, 0, 5
            };
            foreach (int index in triangles) indices.Add(first + index);
        }

        private static void AppendTube(Vector3 start, Vector3 end,
            float radius, Color color, List<Vector3> vertices,
            List<Color32> colors, List<int> indices)
        {
            Vector3 axis = end - start;
            if (axis.sqrMagnitude < 1e-10f) return;
            BuildBasis(axis, out Vector3 tangent0, out Vector3 tangent1);
            const int sides = 6;
            int first = vertices.Count;
            for (int endpoint = 0; endpoint < 2; endpoint++)
            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                Vector3 radial = tangent0 * Mathf.Cos(angle) +
                    tangent1 * Mathf.Sin(angle);
                vertices.Add((endpoint == 0 ? start : end) + radial * radius);
                colors.Add(color);
            }
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                int a = first + side;
                int b = first + next;
                int c = first + sides + next;
                int d = first + sides + side;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(d);
            }
        }

        private void DestroyVisualFor(int id)
        {
            if (!_visuals.TryGetValue(id, out StrokeVisual visual)) return;
            _visuals.Remove(id);
            DestroyVisual(visual);
        }

        private static void DestroyVisual(StrokeVisual visual)
        {
            if (visual.Object != null) DestroyObject(visual.Object);
            if (visual.Mesh != null) DestroyObject(visual.Mesh);
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void MarkChanged()
        {
            _dirty = true;
            Changed?.Invoke();
        }

        internal void MarkDocumentChanged() => MarkChanged();

        private Vector3 WorldToRoomPoint(Vector3 value) => _roomRoot != null
            ? _roomRoot.InverseTransformPoint(value) : value;
        private Vector3 RoomToWorldPoint(Vector3 value) => _roomRoot != null
            ? _roomRoot.TransformPoint(value) : value;
        private Vector3 WorldToRoomDirection(Vector3 value) =>
            _roomRoot != null ? _roomRoot.InverseTransformDirection(value) : value;
        private float WorldToRoomRadius(float value)
        {
            if (_roomRoot == null) return value;
            Vector3 x = _roomRoot.InverseTransformVector(Vector3.right * value);
            Vector3 y = _roomRoot.InverseTransformVector(Vector3.up * value);
            Vector3 z = _roomRoot.InverseTransformVector(Vector3.forward * value);
            return Mathf.Max(x.magnitude, Mathf.Max(y.magnitude, z.magnitude));
        }
        private float RoomToWorldRadius(float value)
        {
            if (_roomRoot == null) return value;
            Vector3 x = _roomRoot.TransformVector(Vector3.right * value);
            Vector3 y = _roomRoot.TransformVector(Vector3.up * value);
            Vector3 z = _roomRoot.TransformVector(Vector3.forward * value);
            return Mathf.Max(x.magnitude, Mathf.Max(y.magnitude, z.magnitude));
        }

        private static Color ApplySaturation(Color color, float multiplier)
        {
            Color.RGBToHSV(color, out float hue, out float saturation,
                out float value);
            Color result = Color.HSVToRGB(hue, Mathf.Clamp01(saturation *
                multiplier), value);
            result.a = color.a;
            return result;
        }

        private static Color StrokeColor(MerkabaDesignStroke stroke)
        {
            Color color = stroke.color;
            color.a = Mathf.Clamp01(stroke.opacity * stroke.flow);
            return color;
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            material.SetColor(BaseColorId, Color.white);
            material.SetFloat(AlphaDitherId, 0f);
            material.SetFloat(SourceBlendId,
                (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat(DestinationBlendId,
                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static void BuildBasis(Vector3 axis, out Vector3 tangent0,
            out Vector3 tangent1)
        {
            Vector3 normal = axis.sqrMagnitude > 1e-10f
                ? axis.normalized : Vector3.forward;
            Vector3 reference = Mathf.Abs(normal.y) < 0.9f
                ? Vector3.up : Vector3.right;
            tangent0 = Vector3.Cross(reference, normal).normalized;
            tangent1 = Vector3.Cross(normal, tangent0).normalized;
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }

        private static float Unit(uint value) =>
            (value & 0x00ffffffu) / 16777216f;

        private sealed class StrokeVisual
        {
            internal readonly GameObject Object;
            internal readonly Mesh Mesh;
            internal readonly List<Vector3> Vertices = new();
            internal readonly List<Color32> Colors = new();
            internal readonly List<int> Indices = new();
            internal int SampleCount;

            internal StrokeVisual(GameObject gameObject, Mesh mesh)
            {
                Object = gameObject;
                Mesh = mesh;
            }
        }

        internal readonly struct PaintHit
        {
            internal readonly Vector3 Point;
            internal readonly Color Color;
            internal readonly int StrokeId;
            internal readonly float Along;

            internal PaintHit(Vector3 point, Color color, int strokeId,
                float along)
            {
                Point = point;
                Color = color;
                StrokeId = strokeId;
                Along = along;
            }
        }

        internal readonly struct PaintInputSample
        {
            internal readonly Vector3 WorldPosition;
            internal readonly Vector3 WorldNormal;
            internal readonly bool HasNormal;
            internal readonly float Radius;

            internal PaintInputSample(Vector3 worldPosition,
                Vector3 worldNormal, bool hasNormal, float radius = -1f)
            {
                WorldPosition = worldPosition;
                WorldNormal = worldNormal;
                HasNormal = hasNormal;
                Radius = radius;
            }
        }
    }
}
