using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Bounded cache of finalized local meshes. Entries are ordinary Unity meshes in their
    /// chunk frames, so pose-graph updates only move GameObjects and never rewrite geometry.
    /// The active GPU Surface Nets mesh is owned by MeshExtractor and is not counted here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersistedChunkMeshCache : MonoBehaviour
    {
        private sealed class CacheEntry
        {
            public string ChunkId;
            public GameObject GameObject;
            public MeshFilter Filter;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public ChunkLiveMeshSnapshot Snapshot;
            public bool WireframeExpanded;
        }

        private sealed class ArtifactLoadResult
        {
            public ChunkLiveMeshSnapshot Snapshot;
            public string Error;
        }

        private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
        private readonly HashSet<string> _suppressedChunks = new(StringComparer.Ordinal);
        private Material _material;
        private bool _ownsMaterial;
        private int _maximumEntries = 2;
        private ScanRenderMode _renderMode = ScanRenderMode.Vertex;
        private readonly GpuResourceRetirementQueue _gpuRetirement = new();

        public int Count => _entries.Count;
        public int MaximumEntries => _maximumEntries;
        public bool Contains(string chunkId) => chunkId != null && _entries.ContainsKey(chunkId);
        public bool IsSuppressed(string chunkId) => chunkId != null &&
            _suppressedChunks.Contains(chunkId);

        /// <summary>
        /// Prevents a coarse live mesh from racing an atomically promoted enhanced renderer.
        /// Removing suppression never fabricates a mesh; the normal bounded restore path may
        /// load it again if that chunk becomes resident.
        /// </summary>
        public void SetSuppressed(string chunkId, bool suppressed)
        {
            if (string.IsNullOrEmpty(chunkId)) return;
            if (suppressed)
            {
                _suppressedChunks.Add(chunkId);
                Remove(chunkId);
            }
            else
            {
                _suppressedChunks.Remove(chunkId);
            }
        }

        /// <summary>Applies the scanner's representation mode to every cached chunk.</summary>
        public void SetRenderMode(ScanRenderMode mode)
        {
            _renderMode = mode;
            bool visible = IsLiveMeshMode(mode);
            bool needsBarycentrics = mode == ScanRenderMode.Wireframe;
            foreach (CacheEntry entry in _entries.Values)
            {
                if (visible && entry.WireframeExpanded != needsBarycentrics &&
                    !TryReplaceRepresentation(entry, needsBarycentrics, out string error))
                    Logger.Warning($"Chunk {entry.ChunkId} representation switch failed: " +
                                   error);
                if (entry.Renderer != null)
                    entry.Renderer.enabled = visible;
            }
        }

        public void Initialize(Material material, int maximumEntries)
        {
            _maximumEntries = Mathf.Max(0, maximumEntries);
            if (_ownsMaterial && _material != null)
                DestroyOwnedObject(_material);
            _ownsMaterial = false;
            _material = material;
            if (_material == null)
            {
                _material = Resources.Load<Material>("PersistedChunkMaterial");
            }
            if (_material == null)
            {
                Shader shader = Shader.Find("Genesis/RoomScan/PersistedChunkVertexColor");
                if (shader != null)
                {
                    _material = new Material(shader) { name = "PersistedChunkMaterial (Runtime)" };
                    _ownsMaterial = true;
                }
            }
            EnforceLimit(Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }

        public bool TryPromote(ChunkRecord chunk, ChunkLiveMeshSnapshot snapshot,
            out string error)
        {
            error = null;
            if (chunk == null || string.IsNullOrEmpty(chunk.chunkId))
            {
                error = "Chunk metadata is required.";
                return false;
            }
            if (_maximumEntries == 0)
                return true;
            if (_suppressedChunks.Contains(chunk.chunkId))
                return true;
            if (_material == null)
            {
                error = "Persisted chunk material/shader is unavailable.";
                return false;
            }
            bool wireframeExpanded = _renderMode == ScanRenderMode.Wireframe;
            if (!TryBuildMesh(snapshot, chunk.chunkId, wireframeExpanded,
                    out Mesh mesh, out error))
                return false;

            Remove(chunk.chunkId);
            var go = new GameObject("Persisted " + chunk.chunkId);
            go.transform.SetParent(transform, false);
            ApplyPose(go.transform, chunk.worldFromChunk);
            go.layer = gameObject.layer;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = IsLiveMeshMode(_renderMode);
            _entries.Add(chunk.chunkId, new CacheEntry
            {
                ChunkId = chunk.chunkId,
                GameObject = go,
                Filter = filter,
                Mesh = mesh,
                Renderer = renderer,
                Snapshot = snapshot,
                WireframeExpanded = wireframeExpanded
            });
            EnforceLimit(Camera.main != null ? Camera.main.transform.position :
                chunk.worldFromChunk.position);
            return true;
        }

        public async Task<string> LoadAsync(WorldStore store, WorldManifest manifest,
            ChunkRecord chunk)
        {
            if (store == null || manifest == null || chunk == null)
                return "Store, manifest, and chunk are required.";
            ChunkArtifactRecord artifact = chunk.artifacts?.Find(candidate =>
                candidate.kind == ChunkArtifactKind.LiveMesh);
            if (artifact == null)
                return "Chunk has no live-mesh artifact.";

            ArtifactLoadResult loaded = await Task.Run(() => LoadArtifact(
                store, manifest.worldId, artifact));
            if (loaded.Snapshot == null)
                return loaded.Error ?? "Live-mesh artifact could not be loaded.";
            return TryPromote(chunk, loaded.Snapshot, out string promotionError)
                ? null
                : promotionError;
        }

        public async Task RestoreNearestAsync(WorldStore store, WorldManifest manifest,
            Vector3 cameraWorldPosition)
        {
            if (store == null || manifest?.chunks == null || _maximumEntries <= 0)
                return;
            var candidates = new List<ChunkRecord>();
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord chunk = manifest.chunks[i];
                if (chunk != null && chunk.state != ChunkLifecycleState.Active &&
                    !_suppressedChunks.Contains(chunk.chunkId) &&
                    chunk.artifacts?.Exists(artifact =>
                        artifact.kind == ChunkArtifactKind.LiveMesh) == true)
                    candidates.Add(chunk);
            }
            candidates.Sort((left, right) =>
            {
                float leftDistance = (left.worldFromChunk.position - cameraWorldPosition).sqrMagnitude;
                float rightDistance = (right.worldFromChunk.position - cameraWorldPosition).sqrMagnitude;
                int comparison = leftDistance.CompareTo(rightDistance);
                return comparison != 0 ? comparison : string.CompareOrdinal(left.chunkId,
                    right.chunkId);
            });

            int count = Mathf.Min(_maximumEntries, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                if (_entries.ContainsKey(candidates[i].chunkId))
                    continue;
                string error = await LoadAsync(store, manifest, candidates[i]);
                if (!string.IsNullOrEmpty(error))
                    Logger.Warning($"Chunk mesh restore rejected ({candidates[i].chunkId}): " +
                                   error);
            }
            EnforceLimit(cameraWorldPosition);
        }

        public void RefreshTransforms(WorldManifest manifest)
        {
            if (manifest?.chunks == null)
                return;
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord chunk = manifest.chunks[i];
                if (chunk != null && _entries.TryGetValue(chunk.chunkId, out CacheEntry entry))
                    ApplyPose(entry.GameObject.transform, chunk.worldFromChunk);
            }
        }

        public void Clear()
        {
            var ids = new List<string>(_entries.Keys);
            for (int i = 0; i < ids.Count; i++)
                Remove(ids[i]);
            _suppressedChunks.Clear();
        }

        private static ArtifactLoadResult LoadArtifact(WorldStore store, string worldId,
            ChunkArtifactRecord artifact)
        {
            if (!store.TryResolveVerifiedArtifact(worldId, artifact, out string path,
                    out string error))
                return new ArtifactLoadResult { Error = error };
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                return ChunkSnapshotCodec.TryReadLiveMesh(stream,
                    out ChunkLiveMeshSnapshot snapshot, out error)
                    ? new ArtifactLoadResult { Snapshot = snapshot }
                    : new ArtifactLoadResult { Error = error };
            }
            catch (Exception exception)
            {
                return new ArtifactLoadResult
                {
                    Error = "Live-mesh artifact read failed: " + exception.Message
                };
            }
        }

        private static bool TryBuildMesh(ChunkLiveMeshSnapshot snapshot, string chunkId,
            bool expandForWireframe, out Mesh mesh, out string error)
        {
            mesh = null;
            error = null;
            if (snapshot == null || snapshot.VertexCount <= 0 || snapshot.IndexCount <= 0 ||
                snapshot.VertexBytes == null || snapshot.IndexBytes == null ||
                snapshot.VertexBytes.Length != snapshot.VertexCount *
                    ChunkLiveMeshSnapshot.VertexStride ||
                snapshot.IndexBytes.Length != snapshot.IndexCount * sizeof(uint))
            {
                error = "Live-mesh snapshot is incomplete.";
                return false;
            }

            var positions = new Vector3[snapshot.VertexCount];
            var normals = new Vector3[snapshot.VertexCount];
            var colors = new Color32[snapshot.VertexCount];
            for (int i = 0; i < snapshot.VertexCount; i++)
            {
                int offset = i * ChunkLiveMeshSnapshot.VertexStride;
                var position = new Vector3(
                    BitConverter.ToSingle(snapshot.VertexBytes, offset),
                    BitConverter.ToSingle(snapshot.VertexBytes, offset + 4),
                    BitConverter.ToSingle(snapshot.VertexBytes, offset + 8));
                var normal = new Vector3(
                    BitConverter.ToSingle(snapshot.VertexBytes, offset + 12),
                    BitConverter.ToSingle(snapshot.VertexBytes, offset + 16),
                    BitConverter.ToSingle(snapshot.VertexBytes, offset + 20));
                if (!IsFinite(position) || !IsFinite(normal))
                {
                    error = $"Live-mesh vertex {i} contains a non-finite value.";
                    return false;
                }
                uint packed = BitConverter.ToUInt32(snapshot.VertexBytes, offset + 24);
                positions[i] = position;
                normals[i] = normal;
                colors[i] = new Color32((byte)(packed & 0xFF),
                    (byte)((packed >> 8) & 0xFF), (byte)((packed >> 16) & 0xFF),
                    (byte)((packed >> 24) & 0xFF));
            }

            var indices = new int[snapshot.IndexCount];
            for (int i = 0; i < indices.Length; i++)
            {
                uint value = BitConverter.ToUInt32(snapshot.IndexBytes, i * sizeof(uint));
                if (value >= snapshot.VertexCount)
                {
                    error = $"Live-mesh index {i} is outside the vertex array.";
                    return false;
                }
                indices[i] = (int)value;
            }

            if (expandForWireframe)
            {
                // Barycentrics are a per-triangle-corner attribute, so this expansion is
                // required only while wireframe is actually selected. Vertex/triplanar
                // modes keep the compact indexed Surface Nets topology.
                int cornerCount = indices.Length;
                var expandedPositions = new Vector3[cornerCount];
                var expandedNormals = new Vector3[cornerCount];
                var expandedColors = new Color32[cornerCount];
                var expandedIndices = new int[cornerCount];
                var barycentrics = new List<Vector3>(cornerCount);
                for (int i = 0; i < cornerCount; i++)
                {
                    int sourceIndex = indices[i];
                    expandedPositions[i] = positions[sourceIndex];
                    expandedNormals[i] = normals[sourceIndex];
                    expandedColors[i] = colors[sourceIndex];
                    expandedIndices[i] = i;
                    barycentrics.Add(i % 3 == 0 ? new Vector3(1f, 0f, 0f) :
                        i % 3 == 1 ? new Vector3(0f, 1f, 0f) :
                                     new Vector3(0f, 0f, 1f));
                }

                mesh = new Mesh
                {
                    name = "Chunk " + chunkId + " (wireframe)",
                    indexFormat = cornerCount > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                mesh.vertices = expandedPositions;
                mesh.normals = expandedNormals;
                mesh.colors32 = expandedColors;
                mesh.SetUVs(1, barycentrics);
                mesh.SetIndices(expandedIndices, MeshTopology.Triangles, 0, false);
            }
            else
            {
                mesh = new Mesh
                {
                    name = "Chunk " + chunkId,
                    indexFormat = snapshot.VertexCount > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                mesh.vertices = positions;
                mesh.normals = normals;
                mesh.colors32 = colors;
                mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            }
            mesh.bounds = snapshot.LocalBounds.ToUnityBounds();
            mesh.UploadMeshData(true);
            return true;
        }

        private bool TryReplaceRepresentation(CacheEntry entry, bool wireframe,
            out string error)
        {
            error = null;
            if (entry == null || entry.Filter == null || entry.Snapshot == null)
            {
                error = "Cached source snapshot is unavailable.";
                return false;
            }
            if (!TryBuildMesh(entry.Snapshot, entry.ChunkId, wireframe,
                    out Mesh replacement, out error))
                return false;

            Mesh previous = entry.Mesh;
            entry.Filter.sharedMesh = replacement;
            entry.Mesh = replacement;
            entry.WireframeExpanded = wireframe;
            if (previous != null)
                _gpuRetirement.RetireAfterCurrentGpuWork(previous);
            return true;
        }

        private void EnforceLimit(Vector3 cameraWorldPosition)
        {
            while (_entries.Count > _maximumEntries)
            {
                CacheEntry farthest = null;
                float farthestDistance = float.NegativeInfinity;
                foreach (CacheEntry entry in _entries.Values)
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
                if (farthest == null)
                    break;
                Remove(farthest.ChunkId);
            }
        }

        public void Remove(string chunkId)
        {
            if (!_entries.Remove(chunkId, out CacheEntry entry))
                return;
            if (entry.Renderer != null)
                entry.Renderer.enabled = false;
            if (entry.Filter != null)
                entry.Filter.sharedMesh = null;
            if (entry.GameObject != null)
                DestroyOwnedObject(entry.GameObject);
            if (entry.Mesh != null)
                _gpuRetirement.RetireAfterCurrentGpuWork(entry.Mesh);
        }

        private void LateUpdate()
        {
            _gpuRetirement.DrainCompleted();
        }

        private static void ApplyPose(Transform target, RigidPoseData worldFromChunk)
        {
            target.SetPositionAndRotation(worldFromChunk.position, worldFromChunk.rotation);
            target.localScale = Vector3.one;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsLiveMeshMode(ScanRenderMode mode)
        {
            return mode == ScanRenderMode.Vertex || mode == ScanRenderMode.Triplanar ||
                   mode == ScanRenderMode.Wireframe;
        }

        private void OnDestroy()
        {
            Clear();
            if (_ownsMaterial && _material != null)
                DestroyOwnedObject(_material);
        }

        private static void DestroyOwnedObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
    }
}
