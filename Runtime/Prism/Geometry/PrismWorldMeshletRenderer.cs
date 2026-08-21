using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Draws the current and bounded resident chunk meshlet generations directly from
    /// GPU buffers. It is display-only: prediction/association still consumes only the
    /// active canonical chunk, so local film IDs from neighbouring chunks cannot alias.
    ///
    /// The draw is registered through <see cref="Graphics.RenderPrimitivesIndirect"/>
    /// from <c>LateUpdate</c>. A command issued from
    /// <c>RenderPipelineManager.beginCameraRendering</c> is too early for URP: the camera
    /// clear/render passes run afterwards and erase the preview in the same frame.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    public sealed class PrismWorldMeshletRenderer : MonoBehaviour
    {
        [SerializeField] private Shader previewShader;
        [SerializeField, Min(4f)] private float conservativeChunkBounds = 12f;

        private static readonly int VerticesId =
            Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId =
            Shader.PropertyToID("_ContactIndices");
        private static readonly int WorldFromChunkId =
            Shader.PropertyToID("_WorldFromChunk");

        private readonly Dictionary<string, ContactMeshletBuffers> _resident =
            new(StringComparer.Ordinal);
        private readonly List<ContactMeshletBuffers> _retiring = new();
        private Material _material;
        private MaterialPropertyBlock _properties;
        private string _activeChunkId;
        private ContactMeshletBuffers _active;

        public int ResidentCount => _resident.Count;
        public bool RenderVisible { get; set; } = true;

        private void OnEnable()
        {
            previewShader ??= Resources.Load<Shader>("Prism/ContactFilmPreview");
            if (previewShader != null && _material == null)
                _material = new Material(previewShader)
                {
                    name = "[Cone-PRISM] World Meshlets",
                    hideFlags = HideFlags.HideAndDontSave
                };
            _properties ??= new MaterialPropertyBlock();
        }

        private void OnDisable() { }

        public void SetActive(string chunkId, ContactMeshletBuffers meshlets)
        {
            _activeChunkId = chunkId;
            _active = meshlets;
        }

        public void RegisterResident(string chunkId, ContactMeshletBuffers meshlets)
        {
            if (string.IsNullOrEmpty(chunkId) || meshlets == null ||
                meshlets.IsDisposed) return;
            if (_resident.TryGetValue(chunkId, out ContactMeshletBuffers previous) &&
                !ReferenceEquals(previous, meshlets))
                Retire(previous);
            _resident[chunkId] = meshlets;
        }

        public void RemoveResident(string chunkId)
        {
            if (!_resident.Remove(chunkId, out ContactMeshletBuffers buffers)) return;
            Retire(buffers);
        }

        public void SetResidentTransform(string chunkId, Matrix4x4 worldFromChunk)
        {
            if (_resident.TryGetValue(chunkId, out ContactMeshletBuffers buffers) &&
                buffers != null && !buffers.IsDisposed)
                buffers.SetChunkTransform(worldFromChunk);
        }

        private void LateUpdate()
        {
            for (int i = _retiring.Count - 1; i >= 0; i--)
            {
                ContactMeshletBuffers buffers = _retiring[i];
                if (buffers == null || buffers.IsDisposed ||
                    buffers.Published.CanWrite)
                {
                    buffers?.Dispose();
                    _retiring.RemoveAt(i);
                }
            }

            if (!RenderVisible || _material == null) return;
            Draw(_active);
            foreach (KeyValuePair<string, ContactMeshletBuffers> pair in _resident)
                if (!string.Equals(pair.Key, _activeChunkId,
                        StringComparison.Ordinal))
                    Draw(pair.Value);
        }

        private void Draw(ContactMeshletBuffers meshlets)
        {
            if (meshlets == null || meshlets.IsDisposed ||
                meshlets.PublicationGeneration == 0u) return;
            ContactMeshletGenerationBuffers published = meshlets.Published;
            _properties.Clear();
            _properties.SetBuffer(VerticesId, published.Vertices);
            _properties.SetBuffer(IndicesId, published.Indices);
            _properties.SetMatrix(WorldFromChunkId, meshlets.WorldFromChunk);
            Vector3 chunkOrigin = meshlets.WorldFromChunk.MultiplyPoint3x4(
                Vector3.zero);
            var renderParams = new RenderParams(_material)
            {
                worldBounds = new Bounds(chunkOrigin,
                    Vector3.one * conservativeChunkBounds),
                matProps = _properties,
                receiveShadows = false,
                shadowCastingMode = ShadowCastingMode.Off,
                layer = gameObject.layer
            };
            Graphics.RenderPrimitivesIndirect(renderParams,
                MeshTopology.Triangles, published.DrawArguments, 1);
            try
            {
                GraphicsFence fence = Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                published.MarkRead(fence);
            }
            catch (Exception) { }
        }

        private void Retire(ContactMeshletBuffers buffers)
        {
            if (buffers != null && !buffers.IsDisposed && !_retiring.Contains(buffers))
                _retiring.Add(buffers);
        }

        private void OnDestroy()
        {
            foreach (ContactMeshletBuffers buffers in _resident.Values)
                buffers?.Dispose();
            foreach (ContactMeshletBuffers buffers in _retiring)
                buffers?.Dispose();
            _resident.Clear();
            _retiring.Clear();
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
