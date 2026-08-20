using System;
using System.Collections.Generic;
using System.Linq;
using Genesis.RoomScan.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.HeavyCompute
{
    /// <summary>
    /// Bounded set of ordinary triangle renderers in chunk-local frames. A complete replacement
    /// is built disabled, then installed and the coarse renderer suppressed in one main-thread
    /// operation. Any validation/build failure destroys only the candidate and preserves the
    /// last-known-good renderer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DiffSoupRendererCache : MonoBehaviour
    {
        private sealed class Entry
        {
            public string ChunkId;
            public int ChunkRevision;
            public string ArtifactSha256;
            public GameObject GameObject;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public Material Material;
            public Texture2D Lut0;
            public Texture2D Lut1;
        }

        private static readonly int Lut0Id = Shader.PropertyToID("_Lut0");
        private static readonly int Lut1Id = Shader.PropertyToID("_Lut1");
        private static readonly int LutSizeId = Shader.PropertyToID("_LutSize");
        private static readonly int LevelId = Shader.PropertyToID("_Level");
        private static readonly int DepthOnlyId = Shader.PropertyToID("_DepthOnly");
        private static readonly int ColorMaskId = Shader.PropertyToID("_ColorMask");
        private static readonly int W1Id = Shader.PropertyToID("_W1");
        private static readonly int B1Id = Shader.PropertyToID("_B1");
        private static readonly int W2Id = Shader.PropertyToID("_W2");
        private static readonly int B2Id = Shader.PropertyToID("_B2");
        private static readonly int W3Id = Shader.PropertyToID("_W3");
        private static readonly int B3Id = Shader.PropertyToID("_B3");

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private RoomScanner _scanner;
        private SubmapManager _submaps;
        private PersistedChunkMeshCache _coarseCache;
        private ChunkRefinementScheduler _scheduler;
        private int _maximumEntries = 2;
        private string _activeChunkId;
        private ScanRenderMode _renderMode = ScanRenderMode.Vertex;

        public int Count => _entries.Count;
        public int MaximumEntries => _maximumEntries;
        public bool Contains(string chunkId) => chunkId != null && _entries.ContainsKey(chunkId);

        public void Initialize(RoomScanner scanner, SubmapManager submaps,
            PersistedChunkMeshCache coarseCache, int maximumEntries)
        {
            Unsubscribe();
            _scanner = scanner;
            _submaps = submaps;
            _coarseCache = coarseCache;
            _maximumEntries = Mathf.Clamp(maximumEntries, 0, 8);
            _scheduler = scanner != null ? scanner.GetComponent<ChunkRefinementScheduler>() :
                GetComponent<ChunkRefinementScheduler>();
            if (_scheduler != null)
                _scheduler.ArtifactPromoted += OnArtifactPromoted;
            if (_submaps != null)
            {
                _submaps.ActiveChunkChanged += OnActiveChunkChanged;
                _activeChunkId = _submaps.ActiveChunk?.chunkId;
            }
            if (_scanner != null)
            {
                _scanner.RenderModeChanged += SetRenderMode;
                _renderMode = _scanner.CurrentRenderMode;
            }
            EnforceLimit(Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            ApplyVisibility();
        }

        public bool TryPromote(ChunkRecord chunk, DiffSoupArtifactData data,
            ChunkArtifactRecord artifact, out string error)
        {
            error = ValidatePromotion(chunk, data, artifact);
            if (error != null) return false;
            if (_maximumEntries == 0)
            {
                error = "DiffSoup renderer cache is disabled.";
                return false;
            }
            if (_entries.TryGetValue(chunk.chunkId, out Entry existing) &&
                existing.ChunkRevision == artifact.chunkRevision &&
                string.Equals(existing.ArtifactSha256, artifact.sha256,
                    StringComparison.Ordinal))
            {
                ApplyPose(existing.GameObject.transform, chunk.worldFromChunk);
                ApplyEntryVisibility(existing);
                return true;
            }

            if (!TryBuildEntry(chunk, data, artifact, out Entry candidate, out error))
                return false;
            // Candidate remains disabled until every resource and uniform is valid.
            candidate.Renderer.enabled = false;
            _entries[chunk.chunkId] = candidate;
            _coarseCache?.SetSuppressed(chunk.chunkId, true);
            ApplyEntryVisibility(candidate);
            if (existing != null)
                DestroyEntry(existing);
            EnforceLimit(Camera.main != null ? Camera.main.transform.position :
                chunk.worldFromChunk.position);
            Logger.Info($"DiffSoup renderer promoted: chunk={chunk.chunkId}, " +
                        $"revision={artifact.chunkRevision}, faces={data.Manifest.model.numFaces}, " +
                        $"resident={_entries.Count}/{_maximumEntries}");
            return true;
        }

        public void SetRenderMode(ScanRenderMode mode)
        {
            _renderMode = mode;
            ApplyVisibility();
        }

        public void RefreshTransforms(WorldManifest manifest)
        {
            if (manifest?.chunks == null) return;
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord chunk = manifest.chunks[i];
                if (chunk != null && _entries.TryGetValue(chunk.chunkId, out Entry entry))
                    ApplyPose(entry.GameObject.transform, chunk.worldFromChunk);
            }
        }

        public void Remove(string chunkId, bool restoreCoarse = false)
        {
            if (!_entries.Remove(chunkId, out Entry entry)) return;
            DestroyEntry(entry);
            _coarseCache?.SetSuppressed(chunkId, false);
            if (restoreCoarse && _submaps?.Store != null && _submaps.Manifest != null)
            {
                ChunkRecord chunk = _submaps.Manifest.chunks.Find(candidate =>
                    candidate != null && string.Equals(candidate.chunkId, chunkId,
                        StringComparison.Ordinal));
                if (chunk != null && chunk.state != ChunkLifecycleState.Active)
                    _ = _coarseCache?.LoadAsync(_submaps.Store, _submaps.Manifest, chunk);
            }
        }

        public void Clear()
        {
            string[] ids = _entries.Keys.ToArray();
            for (int i = 0; i < ids.Length; i++) Remove(ids[i]);
        }

        internal bool TryGetEntryInfo(string chunkId, out int chunkRevision,
            out string artifactSha256, out Transform transform, out MeshRenderer renderer)
        {
            if (_entries.TryGetValue(chunkId, out Entry entry))
            {
                chunkRevision = entry.ChunkRevision;
                artifactSha256 = entry.ArtifactSha256;
                transform = entry.GameObject.transform;
                renderer = entry.Renderer;
                return true;
            }
            chunkRevision = -1;
            artifactSha256 = null;
            transform = null;
            renderer = null;
            return false;
        }

        internal static bool TryDecodeLut(byte[] png, int expectedWidth,
            int expectedHeight, out Texture2D texture, out string error,
            bool markNonReadable = true)
        {
            texture = null;
            error = null;
            if (png == null || expectedWidth < 1 || expectedHeight < 1)
            {
                error = "DiffSoup LUT decode arguments are invalid.";
                return false;
            }
            Texture2D candidate = null;
            try
            {
                candidate = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                {
                    name = "DiffSoup LUT",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };
                if (!ImageConversion.LoadImage(candidate, png, false) ||
                    candidate.width != expectedWidth || candidate.height != expectedHeight)
                {
                    error = $"DiffSoup LUT PNG decode/dimensions are unsupported " +
                            $"(actual={candidate.width}x{candidate.height}, " +
                            $"expected={expectedWidth}x{expectedHeight}).";
                    DestroyOwnedObject(candidate);
                    return false;
                }

                // PIL/WebGL flipY=false treats PNG row zero as LUT y=0. Unity's image loader
                // presents PNG row zero at texture top, so reverse rows once for texelLoad parity.
                Color32[] pixels = candidate.GetPixels32();
                for (int y = 0; y < expectedHeight / 2; y++)
                {
                    int opposite = expectedHeight - 1 - y;
                    for (int x = 0; x < expectedWidth; x++)
                    {
                        int first = y * expectedWidth + x;
                        int second = opposite * expectedWidth + x;
                        (pixels[first], pixels[second]) = (pixels[second], pixels[first]);
                    }
                }
                candidate.SetPixels32(pixels);
                candidate.Apply(false, markNonReadable);
                texture = candidate;
                return true;
            }
            catch (Exception exception)
            {
                DestroyOwnedObject(candidate);
                error = "DiffSoup LUT creation failed: " + exception.Message;
                return false;
            }
        }

        private void OnArtifactPromoted(HeavyComputeQueueItem item,
            DiffSoupArtifactPublishResult result)
        {
            if (!result.Success || _submaps?.Manifest == null ||
                !string.Equals(_submaps.Manifest.worldId,
                    item.submission.key.worldId, StringComparison.Ordinal))
                return;
            ChunkRecord chunk = _submaps.Manifest.chunks.Find(candidate => candidate != null &&
                string.Equals(candidate.chunkId, item.submission.key.chunkId,
                    StringComparison.Ordinal));
            if (chunk == null)
            {
                Logger.Error("DiffSoup renderer promotion rejected: chunk no longer exists");
                return;
            }
            if (!TryPromote(chunk, result.Data, result.Artifact, out string error))
                Logger.Error("DiffSoup renderer promotion rejected: " + error);
        }

        private static string ValidatePromotion(ChunkRecord chunk, DiffSoupArtifactData data,
            ChunkArtifactRecord artifact)
        {
            if (chunk == null || artifact == null ||
                artifact.kind != ChunkArtifactKind.DiffSoup ||
                artifact.formatVersion != HeavyComputeProtocol.DiffSoupArtifactVersion ||
                data?.Manifest?.key == null)
                return "DiffSoup renderer promotion metadata is incomplete.";
            HeavyComputeJobKey key = data.Manifest.key;
            if (!string.Equals(key.chunkId, chunk.chunkId, StringComparison.Ordinal) ||
                key.chunkRevision != artifact.chunkRevision ||
                artifact.chunkRevision > chunk.revision ||
                !Hashing.IsLowerSha256(artifact.sha256))
                return "DiffSoup renderer promotion identity is stale or invalid.";
            return DiffSoupShaderContract.TryValidateRendererData(data, out string error)
                ? null
                : error;
        }

        private bool TryBuildEntry(ChunkRecord chunk, DiffSoupArtifactData data,
            ChunkArtifactRecord artifact, out Entry entry, out string error)
        {
            entry = null;
            error = null;
            Mesh mesh = null;
            Texture2D lut0 = null;
            Texture2D lut1 = null;
            Material material = null;
            GameObject child = null;
            try
            {
                if (!TryBuildMesh(data, chunk.chunkId, out mesh, out error) ||
                    !TryDecodeLut(data.Lut0Png, data.Manifest.model.lutWidth,
                        data.Manifest.model.lutHeight, out lut0, out error) ||
                    !TryDecodeLut(data.Lut1Png, data.Manifest.model.lutWidth,
                        data.Manifest.model.lutHeight, out lut1, out error) ||
                    !DiffSoupShaderContract.TryPackMlp(data.Mlp,
                        out DiffSoupPackedMlp weights, out error))
                    return false;
                Material template = Resources.Load<Material>("DiffSoupMaterial");
                Shader shader = template != null ? template.shader :
                    Shader.Find("Genesis/RoomScan/DiffSoup");
                if (shader == null)
                {
                    error = "DiffSoup URP shader is unavailable or stripped.";
                    return false;
                }
                material = template != null ? new Material(template) : new Material(shader);
                material.name = "DiffSoup " + chunk.chunkId;
                material.SetTexture(Lut0Id, lut0);
                material.SetTexture(Lut1Id, lut1);
                material.SetVector(LutSizeId, new Vector4(data.Manifest.model.lutWidth,
                    data.Manifest.model.lutHeight, 0f, 0f));
                material.SetInt(LevelId, data.Manifest.model.level);
                material.SetMatrixArray(W1Id, weights.W1);
                material.SetVectorArray(B1Id, weights.B1);
                material.SetMatrixArray(W2Id, weights.W2);
                material.SetVectorArray(B2Id, weights.B2);
                material.SetMatrixArray(W3Id, weights.W3);
                material.SetVector(B3Id, weights.B3);
                material.SetFloat(DepthOnlyId, 0f);
                material.SetInt(ColorMaskId, 15);

                child = new GameObject("DiffSoup " + chunk.chunkId);
                child.transform.SetParent(transform, false);
                ApplyPose(child.transform, chunk.worldFromChunk);
                child.layer = gameObject.layer;
                var filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                entry = new Entry
                {
                    ChunkId = chunk.chunkId,
                    ChunkRevision = artifact.chunkRevision,
                    ArtifactSha256 = artifact.sha256,
                    GameObject = child,
                    Mesh = mesh,
                    Renderer = renderer,
                    Material = material,
                    Lut0 = lut0,
                    Lut1 = lut1
                };
                return true;
            }
            catch (Exception exception)
            {
                error = "DiffSoup renderer creation failed: " + exception.Message;
                return false;
            }
            finally
            {
                if (entry == null)
                {
                    DestroyOwnedObject(child);
                    DestroyOwnedObject(mesh);
                    DestroyOwnedObject(material);
                    DestroyOwnedObject(lut0);
                    DestroyOwnedObject(lut1);
                }
            }
        }

        private static bool TryBuildMesh(DiffSoupArtifactData data, string chunkId,
            out Mesh mesh, out string error)
        {
            mesh = null;
            error = null;
            int cornerCount = data.Indices.Length;
            var positions = new Vector3[cornerCount];
            var features = new Vector4[cornerCount];
            var indices = new int[cornerCount];
            Bounds bounds = default;
            for (int corner = 0; corner < cornerCount; corner++)
            {
                Vector3 position = data.Positions[data.Indices[corner]];
                positions[corner] = position;
                int face = corner / 3;
                features[corner] = corner % 3 == 0
                    ? new Vector4(1f, 0f, 0f, face)
                    : corner % 3 == 1
                        ? new Vector4(0f, 1f, 0f, face)
                        : new Vector4(0f, 0f, 1f, face);
                indices[corner] = corner;
                if (corner == 0) bounds = new Bounds(position, Vector3.zero);
                else bounds.Encapsulate(position);
            }
            mesh = new Mesh
            {
                name = "DiffSoup " + chunkId,
                indexFormat = cornerCount > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.vertices = positions;
            mesh.SetUVs(0, features);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = bounds;
            mesh.UploadMeshData(true);
            return true;
        }

        private void OnActiveChunkChanged(ChunkRecord chunk)
        {
            _activeChunkId = chunk?.chunkId;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            foreach (Entry entry in _entries.Values) ApplyEntryVisibility(entry);
        }

        private void ApplyEntryVisibility(Entry entry)
        {
            bool depthOnly = _renderMode == ScanRenderMode.Occlusion;
            entry.Material.SetFloat(DepthOnlyId, depthOnly ? 1f : 0f);
            entry.Material.SetInt(ColorMaskId, depthOnly ? 0 : 15);
            bool modeVisible = _renderMode != ScanRenderMode.None &&
                               _renderMode != ScanRenderMode.Splat;
            entry.Renderer.enabled = modeVisible &&
                !string.Equals(entry.ChunkId, _activeChunkId, StringComparison.Ordinal);
        }

        private void EnforceLimit(Vector3 cameraWorldPosition)
        {
            while (_entries.Count > _maximumEntries)
            {
                Entry farthest = null;
                float farthestDistance = float.NegativeInfinity;
                foreach (Entry entry in _entries.Values)
                {
                    float distance = (entry.GameObject.transform.position -
                                      cameraWorldPosition).sqrMagnitude;
                    if (distance > farthestDistance ||
                        Mathf.Approximately(distance, farthestDistance) &&
                        (farthest == null || string.CompareOrdinal(entry.ChunkId,
                            farthest.ChunkId) > 0))
                    {
                        farthest = entry;
                        farthestDistance = distance;
                    }
                }
                if (farthest == null) break;
                Remove(farthest.ChunkId, true);
            }
        }

        private static void ApplyPose(Transform target, RigidPoseData worldFromChunk)
        {
            target.SetPositionAndRotation(worldFromChunk.position, worldFromChunk.rotation);
            target.localScale = Vector3.one;
        }

        private static void DestroyEntry(Entry entry)
        {
            if (entry == null) return;
            DestroyOwnedObject(entry.GameObject);
            DestroyOwnedObject(entry.Mesh);
            DestroyOwnedObject(entry.Material);
            DestroyOwnedObject(entry.Lut0);
            DestroyOwnedObject(entry.Lut1);
        }

        private void Unsubscribe()
        {
            if (_scheduler != null)
                _scheduler.ArtifactPromoted -= OnArtifactPromoted;
            if (_submaps != null)
                _submaps.ActiveChunkChanged -= OnActiveChunkChanged;
            if (_scanner != null)
                _scanner.RenderModeChanged -= SetRenderMode;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            Clear();
        }

        private static void DestroyOwnedObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
