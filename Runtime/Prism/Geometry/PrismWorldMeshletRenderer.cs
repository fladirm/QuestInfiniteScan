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
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    public sealed class PrismWorldMeshletRenderer : MonoBehaviour
    {
        [SerializeField] private Shader previewShader;

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
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable() =>
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

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
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context,
            Camera camera)
        {
            if (_material == null || camera == null ||
                camera.cameraType != CameraType.Game) return;
            CommandBuffer command = CommandBufferPool.Get(
                "Cone-PRISM World Meshlets");
            try
            {
                Draw(command, _active);
                foreach (KeyValuePair<string, ContactMeshletBuffers> pair in _resident)
                    if (!string.Equals(pair.Key, _activeChunkId,
                            StringComparison.Ordinal))
                        Draw(command, pair.Value);
                context.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private void Draw(CommandBuffer command, ContactMeshletBuffers meshlets)
        {
            if (meshlets == null || meshlets.IsDisposed ||
                meshlets.PublicationGeneration == 0u) return;
            ContactMeshletGenerationBuffers published = meshlets.Published;
            _properties.Clear();
            _properties.SetBuffer(VerticesId, published.Vertices);
            _properties.SetBuffer(IndicesId, published.Indices);
            _properties.SetMatrix(WorldFromChunkId, meshlets.WorldFromChunk);
            command.DrawProceduralIndirect(Matrix4x4.identity, _material, 0,
                MeshTopology.Triangles, published.DrawArguments, 0, _properties);
            try
            {
                GraphicsFence fence = command.CreateGraphicsFence(
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
            OnDisable();
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
