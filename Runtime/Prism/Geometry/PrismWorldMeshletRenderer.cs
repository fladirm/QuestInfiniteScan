using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Materializes the visible portion of each published PressureManifold mesh cache.
    /// The canonical world is never filtered here: per-camera GPU compaction and LOD
    /// affect only derived draw indices.  A measured-contact depth prepass makes the
    /// following translucent colour pass front-most-only, so preview blending cannot
    /// turn coincident generations into an alpha soup.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    public sealed class PrismWorldMeshletRenderer : MonoBehaviour
    {
        [SerializeField] private Shader previewShader;
        [SerializeField] private Shader depthShader;
        [SerializeField] private ComputeShader meshletViewCullCompute;
        [SerializeField, Min(4f)] private float conservativeChunkBounds = 12f;
        [SerializeField, Range(0.25f, 2f)] private float geometryPixelError = 0.65f;
        [SerializeField, Range(-2f, 2f)] private float appearanceMipBias;

        private static readonly int VerticesId =
            Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId =
            Shader.PropertyToID("_ContactIndices");
        private static readonly int WorldFromChunkId =
            Shader.PropertyToID("_WorldFromChunk");
        private static readonly int MeshletDescriptorsId =
            Shader.PropertyToID("_MeshletDescriptors");
        private static readonly int SourceIndicesId =
            Shader.PropertyToID("_SourceIndices");
        private static readonly int BuildCountersId =
            Shader.PropertyToID("_MeshletBuildCounters");
        private static readonly int VisibleIndicesId =
            Shader.PropertyToID("_VisibleIndices");
        private static readonly int VisibleDrawArgumentsId =
            Shader.PropertyToID("_VisibleDrawArguments");
        private static readonly int ViewCountersId =
            Shader.PropertyToID("_ViewCounters");
        private static readonly int ViewLodId =
            Shader.PropertyToID("_MeshletViewLod");
        private static readonly int ClipFromChunkId =
            Shader.PropertyToID("_ClipFromChunk");
        private static readonly int OpticalFromChunkId =
            Shader.PropertyToID("_OpticalFromChunk");
        private static readonly int ViewportSizeId =
            Shader.PropertyToID("_ViewportSize");
        private static readonly int DescriptorCapacityId =
            Shader.PropertyToID("_DescriptorCapacity");
        private static readonly int VisibleIndexCapacityId =
            Shader.PropertyToID("_VisibleIndexCapacity");
        private static readonly int EnableHiZId =
            Shader.PropertyToID("_EnableHiZ");
        private static readonly int HiZMipCountId =
            Shader.PropertyToID("_HiZMipCount");
        private static readonly int EyeId = Shader.PropertyToID("_Eye");
        private static readonly int GeometryPixelErrorId =
            Shader.PropertyToID("_GeometryPixelError");
        private static readonly int AppearanceMipBiasId =
            Shader.PropertyToID("_AppearanceMipBias");
        private static readonly int HiZRangeId = Shader.PropertyToID("_HiZRange");

        private readonly Dictionary<string, ContactMeshletBuffers> _resident =
            new(StringComparer.Ordinal);
        private readonly Dictionary<ContactMeshletBuffers, ContactMeshletViewBuffers>
            _views = new();
        private readonly List<ContactMeshletBuffers> _retiring = new();
        private Material _previewMaterial;
        private Material _depthMaterial;
        private MaterialPropertyBlock _properties;
        private RenderTexture _disabledHiZ;
        private int _clearViewKernel = -1;
        private int _cullViewKernel = -1;
        private int _finalizeViewKernel = -1;
        private string _activeChunkId;
        private ContactMeshletBuffers _active;

        public int ResidentCount => _resident.Count;
        public bool RenderVisible { get; set; } = true;

        private void OnEnable()
        {
            previewShader ??= Resources.Load<Shader>("Prism/ContactFilmPreview");
            depthShader ??= Resources.Load<Shader>("Prism/ContactFilmDepth");
            meshletViewCullCompute ??=
                Resources.Load<ComputeShader>("Prism/MeshletViewCull");
            if (previewShader != null && _previewMaterial == null)
                _previewMaterial = CreateMaterial(previewShader,
                    "[Cone-PRISM] World Meshlets");
            if (depthShader != null && _depthMaterial == null)
                _depthMaterial = CreateMaterial(depthShader,
                    "[Cone-PRISM] Measured Front Depth");
            _properties ??= new MaterialPropertyBlock();
            if (meshletViewCullCompute != null && _clearViewKernel < 0)
            {
                _clearViewKernel = meshletViewCullCompute.FindKernel(
                    "ClearMeshletView");
                _cullViewKernel = meshletViewCullCompute.FindKernel(
                    "CullMeshletView");
                _finalizeViewKernel = meshletViewCullCompute.FindKernel(
                    "FinalizeMeshletView");
            }
            EnsureDisabledHiZ();
        }

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
            CollectRetired();
            if (!RenderVisible || _previewMaterial == null ||
                _depthMaterial == null || meshletViewCullCompute == null) return;
            Camera camera = Camera.main;
            if (camera == null || !camera.isActiveAndEnabled) return;

            Draw(camera, _active);
            foreach (KeyValuePair<string, ContactMeshletBuffers> pair in _resident)
                if (!string.Equals(pair.Key, _activeChunkId,
                        StringComparison.Ordinal))
                    Draw(camera, pair.Value);
        }

        private void Draw(Camera camera, ContactMeshletBuffers meshlets)
        {
            if (meshlets == null || meshlets.IsDisposed ||
                meshlets.PublicationGeneration == 0u) return;
            ContactMeshletGenerationBuffers published = meshlets.Published;
            ContactMeshletViewBuffers view = EnsureView(meshlets);
            if (view == null) return;

            Matrix4x4 worldToView = camera.worldToCameraMatrix;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(
                camera.projectionMatrix, false);
            Matrix4x4 clipFromChunk = projection * worldToView *
                meshlets.WorldFromChunk;
            Matrix4x4 opticalFromChunk = worldToView * meshlets.WorldFromChunk;
            DispatchViewCull(camera, published, view, clipFromChunk,
                opticalFromChunk);

            _properties.Clear();
            _properties.SetBuffer(VerticesId, published.Vertices);
            _properties.SetBuffer(IndicesId, view.VisibleIndices);
            _properties.SetMatrix(WorldFromChunkId, meshlets.WorldFromChunk);
            Vector3 chunkOrigin = meshlets.WorldFromChunk.MultiplyPoint3x4(
                Vector3.zero);
            Bounds bounds = new(chunkOrigin,
                Vector3.one * conservativeChunkBounds);
            Render(_depthMaterial, camera, bounds, view, _properties);
            Render(_previewMaterial, camera, bounds, view, _properties);
            try
            {
                published.MarkRead(Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations));
            }
            catch (Exception) { }
        }

        private void DispatchViewCull(Camera camera,
            ContactMeshletGenerationBuffers published,
            ContactMeshletViewBuffers view, Matrix4x4 clipFromChunk,
            Matrix4x4 opticalFromChunk)
        {
            CommandBuffer command = CommandBufferPool.Get(
                "Cone-PRISM Cull World Meshlets");
            try
            {
                int[] kernels =
                    { _clearViewKernel, _cullViewKernel, _finalizeViewKernel };
                foreach (int kernel in kernels)
                {
                    command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                        VisibleDrawArgumentsId, view.DrawArguments);
                    command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                        ViewCountersId, view.Counters);
                    command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                        VisibleIndicesId, view.VisibleIndices);
                    command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                        ViewLodId, view.ViewLod);
                }
                command.SetComputeBufferParam(meshletViewCullCompute, _cullViewKernel,
                    MeshletDescriptorsId, published.Descriptors);
                command.SetComputeBufferParam(meshletViewCullCompute, _cullViewKernel,
                    SourceIndicesId, published.Indices);
                command.SetComputeBufferParam(meshletViewCullCompute, _cullViewKernel,
                    BuildCountersId, published.BuildCounters);
                command.SetComputeMatrixParam(meshletViewCullCompute,
                    ClipFromChunkId, clipFromChunk);
                command.SetComputeMatrixParam(meshletViewCullCompute,
                    OpticalFromChunkId, opticalFromChunk);
                command.SetComputeVectorParam(meshletViewCullCompute, ViewportSizeId,
                    new Vector4(Mathf.Max(1, camera.pixelWidth),
                        Mathf.Max(1, camera.pixelHeight), 0f, 0f));
                command.SetComputeIntParam(meshletViewCullCompute,
                    DescriptorCapacityId, published.DescriptorCapacity);
                command.SetComputeIntParam(meshletViewCullCompute,
                    VisibleIndexCapacityId, view.IndexCapacity);
                command.SetComputeIntParam(meshletViewCullCompute, EyeId, 0);
                command.SetComputeFloatParam(meshletViewCullCompute,
                    GeometryPixelErrorId, geometryPixelError);
                command.SetComputeFloatParam(meshletViewCullCompute,
                    AppearanceMipBiasId, appearanceMipBias);
                command.SetComputeIntParam(meshletViewCullCompute, EnableHiZId, 0);
                command.SetComputeIntParam(meshletViewCullCompute, HiZMipCountId, 0);
                command.SetComputeTextureParam(meshletViewCullCompute,
                    _cullViewKernel, HiZRangeId, _disabledHiZ);
                command.DispatchCompute(meshletViewCullCompute,
                    _clearViewKernel, 1, 1, 1);
                command.DispatchCompute(meshletViewCullCompute, _cullViewKernel,
                    published.CullDispatchArguments, 0);
                command.DispatchCompute(meshletViewCullCompute,
                    _finalizeViewKernel, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private ContactMeshletViewBuffers EnsureView(ContactMeshletBuffers meshlets)
        {
            if (_views.TryGetValue(meshlets, out ContactMeshletViewBuffers view))
            {
                if (view.IndexCapacity >= meshlets.IndexCapacity &&
                    view.DescriptorCapacity >= meshlets.DescriptorCapacity)
                    return view;
                view.Dispose();
            }
            view = meshlets.CreateViewBuffers();
            _views[meshlets] = view;
            return view;
        }

        private static void Render(Material material, Camera camera, Bounds bounds,
            ContactMeshletViewBuffers view, MaterialPropertyBlock properties)
        {
            var renderParams = new RenderParams(material)
            {
                camera = camera,
                worldBounds = bounds,
                matProps = properties,
                receiveShadows = false,
                shadowCastingMode = ShadowCastingMode.Off,
                layer = camera.gameObject.layer
            };
            Graphics.RenderPrimitivesIndirect(renderParams,
                MeshTopology.Triangles, view.DrawArguments, 1);
        }

        private void CollectRetired()
        {
            for (int i = _retiring.Count - 1; i >= 0; i--)
            {
                ContactMeshletBuffers buffers = _retiring[i];
                if (buffers != null && !buffers.IsDisposed &&
                    !buffers.Published.CanWrite) continue;
                DisposeView(buffers);
                buffers?.Dispose();
                _retiring.RemoveAt(i);
            }
        }

        private void Retire(ContactMeshletBuffers buffers)
        {
            if (buffers != null && !buffers.IsDisposed && !_retiring.Contains(buffers))
                _retiring.Add(buffers);
        }

        private void DisposeView(ContactMeshletBuffers buffers)
        {
            if (buffers != null && _views.Remove(buffers,
                    out ContactMeshletViewBuffers view))
                view.Dispose();
        }

        private void EnsureDisabledHiZ()
        {
            if (_disabledHiZ != null && _disabledHiZ.IsCreated()) return;
            _disabledHiZ = new RenderTexture(1, 1, 0,
                RenderTextureFormat.RFloat)
            {
                name = "[Cone-PRISM] Disabled Hi-Z",
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            _disabledHiZ.Create();
        }

        private static Material CreateMaterial(Shader shader, string materialName) =>
            new(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave
            };

        private void OnDestroy()
        {
            foreach (ContactMeshletViewBuffers view in _views.Values)
                view?.Dispose();
            _views.Clear();
            foreach (ContactMeshletBuffers buffers in _resident.Values)
                buffers?.Dispose();
            foreach (ContactMeshletBuffers buffers in _retiring)
                buffers?.Dispose();
            _resident.Clear();
            _retiring.Clear();
            DestroyResource(_previewMaterial);
            DestroyResource(_depthMaterial);
            _disabledHiZ?.Release();
            DestroyResource(_disabledHiZ);
            _previewMaterial = null;
            _depthMaterial = null;
            _disabledHiZ = null;
        }

        private static void DestroyResource(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }
    }
}
