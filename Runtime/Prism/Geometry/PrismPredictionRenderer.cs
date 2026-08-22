using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Renders the published ContactFilm meshlet generation into dual-depth-eye MRTs.
    /// Hardware Z is the first-hit association accelerator; empty geometry is valid and
    /// yields an all-zero prediction for initial film spawning.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]
    public sealed class PrismPredictionRenderer : MonoBehaviour
    {
        [SerializeField] private PrismDepthPreprocessor depthPreprocessor;
        [SerializeField] private Shader predictionShader;
        [SerializeField] private ComputeShader meshletViewCullCompute;
        [SerializeField] private ComputeShader hiZRangeCompute;
        [SerializeField, Range(3, 12)] private int targetRingSlots = 4;
        [SerializeField, Range(0.25f, 2f)] private float geometryPixelError = 0.65f;
        [SerializeField, Range(-2f, 2f)] private float appearanceMipBias;

        private static readonly int VerticesId = Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId = Shader.PropertyToID("_ContactIndices");
        private static readonly int ViewLodId = Shader.PropertyToID("_MeshletViewLod");
        private static readonly int ClipFromChunkId = Shader.PropertyToID("_ClipFromChunk");
        private static readonly int OpticalFromChunkId = Shader.PropertyToID("_OpticalFromChunk");
        private static readonly int PeelDepthId = Shader.PropertyToID("_PeelDepth");
        private static readonly int PeelEyeId = Shader.PropertyToID("_PeelEye");
        private static readonly int PeelEnabledId = Shader.PropertyToID("_PeelEnabled");
        private static readonly int MeshletDescriptorsId = Shader.PropertyToID("_MeshletDescriptors");
        private static readonly int SourceIndicesId = Shader.PropertyToID("_SourceIndices");
        private static readonly int BuildCountersId = Shader.PropertyToID("_MeshletBuildCounters");
        private static readonly int VisibleIndicesId = Shader.PropertyToID("_VisibleIndices");
        private static readonly int VisibleDrawArgumentsId = Shader.PropertyToID("_VisibleDrawArguments");
        private static readonly int ViewCountersId = Shader.PropertyToID("_ViewCounters");
        private static readonly int ViewportSizeId = Shader.PropertyToID("_ViewportSize");
        private static readonly int DescriptorCapacityId = Shader.PropertyToID("_DescriptorCapacity");
        private static readonly int VisibleIndexCapacityId = Shader.PropertyToID("_VisibleIndexCapacity");
        private static readonly int EnableHiZId = Shader.PropertyToID("_EnableHiZ");
        private static readonly int HiZMipCountId = Shader.PropertyToID("_HiZMipCount");
        private static readonly int EyeId = Shader.PropertyToID("_Eye");
        private static readonly int GeometryPixelErrorId = Shader.PropertyToID("_GeometryPixelError");
        private static readonly int AppearanceMipBiasId = Shader.PropertyToID("_AppearanceMipBias");
        private static readonly int HiZRangeId = Shader.PropertyToID("_HiZRange");
        private static readonly int DepthSigmaSourceId = Shader.PropertyToID("_DepthSigma");
        private static readonly int HiZSourceId = Shader.PropertyToID("_HiZSource");
        private static readonly int HiZDestinationId = Shader.PropertyToID("_HiZDestination");
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int DestinationSizeId = Shader.PropertyToID("_DestinationSize");

        private readonly RenderTargetIdentifier[] _mrt = new RenderTargetIdentifier[4];
        private PredictionTargetRing _targets;
        private PredictionFrameLease _latest;
        private Material _material;
        private MaterialPropertyBlock _properties;
        private ContactMeshletBuffers _meshlets;
        private ContactMeshletViewBuffers _viewBuffers;
        private int _clearViewKernel = -1;
        private int _cullViewKernel = -1;
        private int _finalizeViewKernel = -1;
        private int _copyHiZKernel = -1;
        private int _reduceHiZKernel = -1;
        private bool _rendering;
        private bool _subscribedToSource;
        private long _rendered;
        private long _backpressure;

        public event Action<PredictionFrameLease> PredictionReady;

        public ContactMeshletBuffers Meshlets => _meshlets;
        public long RenderedFrames => _rendered;
        public long BackpressureFrames => _backpressure;

        public bool TryAcquireLatest(out PredictionFrameLease frame)
        {
            if (_latest == null || _latest.IsDisposed)
            {
                frame = null;
                return false;
            }
            frame = _latest.Retain();
            return true;
        }

        public void StartRendering(PrismDepthPreprocessor source = null,
            bool subscribeToSource = true)
        {
            if (_rendering) return;
            depthPreprocessor = source != null ? source : depthPreprocessor;
            depthPreprocessor ??= GetComponent<PrismDepthPreprocessor>();
            if (depthPreprocessor == null)
            {
                Logger.Error("Cone-PRISM prediction requires PrismDepthPreprocessor.");
                return;
            }
            predictionShader ??= Resources.Load<Shader>("Prism/PredictContactFilm");
            meshletViewCullCompute ??=
                Resources.Load<ComputeShader>("Prism/MeshletViewCull");
            hiZRangeCompute ??=
                Resources.Load<ComputeShader>("Prism/HiZRangePyramid");
            if (predictionShader == null || meshletViewCullCompute == null ||
                hiZRangeCompute == null)
            {
                Logger.Error("Cone-PRISM prediction shader is missing.");
                return;
            }
            _material ??= new Material(predictionShader)
            {
                name = "[Cone-PRISM] Prediction Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            _properties ??= new MaterialPropertyBlock();
            _targets ??= new PredictionTargetRing(targetRingSlots);
            _meshlets ??= new ContactMeshletBuffers();
            _clearViewKernel = meshletViewCullCompute.FindKernel("ClearMeshletView");
            _cullViewKernel = meshletViewCullCompute.FindKernel("CullMeshletView");
            _finalizeViewKernel = meshletViewCullCompute.FindKernel("FinalizeMeshletView");
            _copyHiZKernel = hiZRangeCompute.FindKernel("CopyHiZLevelZero");
            _reduceHiZKernel = hiZRangeCompute.FindKernel("ReduceHiZLevel");
            if (subscribeToSource)
            {
                depthPreprocessor.FrameReady += OnDepthFrame;
                _subscribedToSource = true;
            }
            _rendering = true;
        }

        public void StopRendering()
        {
            if (_subscribedToSource && depthPreprocessor != null)
                depthPreprocessor.FrameReady -= OnDepthFrame;
            _subscribedToSource = false;
            _rendering = false;
            _latest?.Dispose();
            _latest = null;
            _targets?.Dispose();
            _targets = null;
            _viewBuffers?.Dispose();
            _viewBuffers = null;
        }

        private void OnDestroy()
        {
            StopRendering();
            _meshlets?.Dispose();
            _meshlets = null;
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
                _material = null;
            }
        }

        private void OnDepthFrame(NormalizedRigFrameLease source) =>
            TryRenderFrame(source, out _);

        internal bool TryRenderFrame(NormalizedRigFrameLease source,
            out PredictionFrameLease renderedFrame)
        {
            renderedFrame = null;
            if (!_rendering || source == null || !source.IsValid) return false;
            if (!_targets.TryBegin(source, out PredictionFrameLease prediction))
            {
                _backpressure++;
                return false;
            }

            CommandBuffer command = CommandBufferPool.Get("Cone-PRISM Predict ContactFilm");
            try
            {
                bool hasPublishedGeometry = _meshlets.PublicationGeneration != 0u;
                if (hasPublishedGeometry)
                {
                    EnsureViewBuffers();
                    BindMeshlets();
                }
                bool hasPreviousHiZ = _latest != null && !_latest.IsDisposed;
                RenderTexture previousHiZ = hasPreviousHiZ
                    ? _latest.HiZRange
                    : prediction.HiZRange;
                for (int eye = 0; eye < 2; eye++)
                {
                    GpuImageView view = eye == 0
                        ? source.Source.DepthLeft
                        : source.Source.DepthRight;
                    Matrix4x4 opticalFromWorld = Matrix4x4.TRS(
                        view.WorldFromCamera.position,
                        view.WorldFromCamera.rotation, Vector3.one).inverse;
                    Matrix4x4 clipFromWorld = BuildClipFromWorld(view, opticalFromWorld);
                    _properties.SetMatrix(ClipFromChunkId,
                        clipFromWorld * _meshlets.WorldFromChunk);
                    _properties.SetMatrix(OpticalFromChunkId,
                        opticalFromWorld * _meshlets.WorldFromChunk);
                    _properties.SetFloat(PeelEnabledId, 0f);
                    SetMrt(prediction.DepthSigma, prediction.NormalConfidence,
                        prediction.FilmIdGeneration, prediction.UvMetadata);
                    command.SetRenderTarget(_mrt,
                        new RenderTargetIdentifier(prediction.HardwareDepth), 0,
                        CubemapFace.Unknown, eye);
                    command.ClearRenderTarget(true, true, Color.clear, 1f);
                    if (hasPublishedGeometry)
                    {
                        BuildView(command, clipFromWorld * _meshlets.WorldFromChunk,
                            opticalFromWorld * _meshlets.WorldFromChunk,
                            view.Resolution, eye, previousHiZ, hasPreviousHiZ);
                        command.DrawProceduralIndirect(Matrix4x4.identity, _material,
                            0, MeshTopology.Triangles,
                            _viewBuffers.DrawArguments, 0, _properties);
                    }

                    // Second visible ContactFilm layer.  The first depth texture is
                    // immutable during this draw, so the fragment shader peels every
                    // first-layer contact before hardware Z selects the next one.
                    _properties.SetFloat(PeelEnabledId, 1f);
                    _properties.SetInt(PeelEyeId, eye);
                    _properties.SetTexture(PeelDepthId, prediction.DepthSigma);
                    SetMrt(prediction.Layer1DepthSigma,
                        prediction.Layer1NormalConfidence,
                        prediction.Layer1FilmIdGeneration,
                        prediction.Layer1UvMetadata);
                    command.SetRenderTarget(_mrt,
                        new RenderTargetIdentifier(prediction.Layer1HardwareDepth), 0,
                        CubemapFace.Unknown, eye);
                    command.ClearRenderTarget(true, true, Color.clear, 1f);
                    // Occlusion against the previous first-hit pyramid is valid for
                    // the front layer, but the peeled layer must retain candidates
                    // behind it. Rebuild the same GPU list with Hi-Z disabled.
                    if (hasPublishedGeometry)
                    {
                        BuildView(command, clipFromWorld * _meshlets.WorldFromChunk,
                            opticalFromWorld * _meshlets.WorldFromChunk,
                            view.Resolution, eye, prediction.HiZRange, false);
                        command.DrawProceduralIndirect(Matrix4x4.identity, _material,
                            0, MeshTopology.Triangles,
                            _viewBuffers.DrawArguments, 0, _properties);
                    }
                }
                BuildHiZ(command, prediction);
                Graphics.ExecuteCommandBuffer(command);
                try
                {
                    if (hasPublishedGeometry)
                        _meshlets.MarkPublishedRead(Graphics.CreateGraphicsFence(
                            GraphicsFenceType.AsyncQueueSynchronisation,
                            SynchronisationStageFlags.AllGPUOperations));
                }
                catch (Exception) { }
                prediction.CommitGpuWrite();

                PredictionFrameLease previous = _latest;
                _latest = prediction;
                previous?.Dispose();
                _rendered++;
                renderedFrame = prediction;
                PredictionReady?.Invoke(prediction);
                return true;
            }
            catch (Exception exception)
            {
                prediction.Dispose();
                Logger.Error($"Cone-PRISM prediction raster failed: {exception.Message}");
                return false;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private void BindMeshlets()
        {
            _properties.Clear();
            _properties.SetBuffer(VerticesId, _meshlets.Vertices);
            _properties.SetBuffer(IndicesId, _viewBuffers.VisibleIndices);
            _properties.SetBuffer(ViewLodId, _viewBuffers.ViewLod);
        }

        private void EnsureViewBuffers()
        {
            if (_viewBuffers != null &&
                _viewBuffers.IndexCapacity >= _meshlets.IndexCapacity &&
                _viewBuffers.DescriptorCapacity >= _meshlets.DescriptorCapacity) return;
            _viewBuffers?.Dispose();
            _viewBuffers = _meshlets.CreateViewBuffers();
        }

        private void BuildView(CommandBuffer command, Matrix4x4 clipFromChunk,
            Matrix4x4 opticalFromChunk, Vector2Int viewport, int eye,
            RenderTexture hiZ, bool allowHiZ)
        {
            ContactMeshletGenerationBuffers published = _meshlets.Published;
            int[] kernels = { _clearViewKernel, _cullViewKernel, _finalizeViewKernel };
            foreach (int kernel in kernels)
            {
                command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                    VisibleDrawArgumentsId, _viewBuffers.DrawArguments);
                command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                    ViewCountersId, _viewBuffers.Counters);
                command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                    VisibleIndicesId, _viewBuffers.VisibleIndices);
                command.SetComputeBufferParam(meshletViewCullCompute, kernel,
                    ViewLodId, _viewBuffers.ViewLod);
            }
            command.SetComputeBufferParam(meshletViewCullCompute, _cullViewKernel,
                MeshletDescriptorsId, published.Descriptors);
            command.SetComputeBufferParam(meshletViewCullCompute, _cullViewKernel,
                SourceIndicesId, published.Indices);
            command.SetComputeBufferParam(meshletViewCullCompute, _cullViewKernel,
                BuildCountersId, published.BuildCounters);
            command.SetComputeMatrixParam(meshletViewCullCompute, ClipFromChunkId,
                clipFromChunk);
            command.SetComputeMatrixParam(meshletViewCullCompute, OpticalFromChunkId,
                opticalFromChunk);
            command.SetComputeVectorParam(meshletViewCullCompute, ViewportSizeId,
                new Vector4(viewport.x, viewport.y, 0f, 0f));
            command.SetComputeIntParam(meshletViewCullCompute, DescriptorCapacityId,
                published.DescriptorCapacity);
            command.SetComputeIntParam(meshletViewCullCompute, VisibleIndexCapacityId,
                _viewBuffers.IndexCapacity);
            command.SetComputeIntParam(meshletViewCullCompute, EyeId, eye);
            command.SetComputeFloatParam(meshletViewCullCompute, GeometryPixelErrorId,
                geometryPixelError);
            command.SetComputeFloatParam(meshletViewCullCompute, AppearanceMipBiasId,
                appearanceMipBias);
            bool useHiZ = allowHiZ && hiZ != null && hiZ.IsCreated();
            command.SetComputeIntParam(meshletViewCullCompute, EnableHiZId,
                useHiZ ? 1 : 0);
            command.SetComputeIntParam(meshletViewCullCompute, HiZMipCountId,
                useHiZ ? hiZ.mipmapCount : 0);
            if (hiZ != null)
                command.SetComputeTextureParam(meshletViewCullCompute,
                    _cullViewKernel, HiZRangeId, hiZ);
            command.DispatchCompute(meshletViewCullCompute, _clearViewKernel, 1, 1, 1);
            command.DispatchCompute(meshletViewCullCompute, _cullViewKernel,
                published.CullDispatchArguments, 0);
            command.DispatchCompute(meshletViewCullCompute, _finalizeViewKernel,
                1, 1, 1);
        }

        private void BuildHiZ(CommandBuffer command, PredictionFrameLease prediction)
        {
            RenderTexture pyramid = prediction.HiZRange;
            Vector2Int size = prediction.Source.Source.DepthLeft.Resolution;
            command.SetComputeVectorParam(hiZRangeCompute, SourceSizeId,
                new Vector4(size.x, size.y, 0f, 0f));
            command.SetComputeVectorParam(hiZRangeCompute, DestinationSizeId,
                new Vector4(size.x, size.y, 0f, 0f));
            command.SetComputeTextureParam(hiZRangeCompute, _copyHiZKernel,
                DepthSigmaSourceId, prediction.DepthSigma);
            command.SetComputeTextureParam(hiZRangeCompute, _copyHiZKernel,
                HiZDestinationId, pyramid, 0);
            command.DispatchCompute(hiZRangeCompute, _copyHiZKernel,
                CeilDiv(size.x, 8), CeilDiv(size.y, 8), 2);
            for (int mip = 1; mip < pyramid.mipmapCount; mip++)
            {
                Vector2Int destination = new(Mathf.Max(1, size.x >> mip),
                    Mathf.Max(1, size.y >> mip));
                Vector2Int source = new(Mathf.Max(1, size.x >> (mip - 1)),
                    Mathf.Max(1, size.y >> (mip - 1)));
                command.SetComputeVectorParam(hiZRangeCompute, SourceSizeId,
                    new Vector4(source.x, source.y, 0f, 0f));
                command.SetComputeVectorParam(hiZRangeCompute, DestinationSizeId,
                    new Vector4(destination.x, destination.y, 0f, 0f));
                command.SetComputeTextureParam(hiZRangeCompute, _reduceHiZKernel,
                    HiZSourceId, pyramid, mip - 1);
                command.SetComputeTextureParam(hiZRangeCompute, _reduceHiZKernel,
                    HiZDestinationId, pyramid, mip);
                command.DispatchCompute(hiZRangeCompute, _reduceHiZKernel,
                    CeilDiv(destination.x, 8), CeilDiv(destination.y, 8), 2);
            }
        }

        private static int CeilDiv(int value, int divisor) =>
            (value + divisor - 1) / divisor;

        private void SetMrt(RenderTexture depthSigma, RenderTexture normalConfidence,
            RenderTexture idGeneration, RenderTexture uvMetadata)
        {
            _mrt[0] = new RenderTargetIdentifier(depthSigma);
            _mrt[1] = new RenderTargetIdentifier(normalConfidence);
            _mrt[2] = new RenderTargetIdentifier(idGeneration);
            _mrt[3] = new RenderTargetIdentifier(uvMetadata);
        }

        private static Matrix4x4 BuildClipFromWorld(GpuImageView view,
            Matrix4x4 opticalFromWorld)
        {
            RigIntrinsics intrinsics = view.Intrinsics;
            Vector2Int resolution = intrinsics.ImageResolution;
            Vector2 nearFar = view.DepthNearFar;
            float rasterFar = RigDepthContract.FiniteRasterFar(nearFar);
            float left = -intrinsics.PrincipalPoint.x / intrinsics.FocalLength.x * nearFar.x;
            float right = (resolution.x - intrinsics.PrincipalPoint.x) /
                          intrinsics.FocalLength.x * nearFar.x;
            float bottom = -intrinsics.PrincipalPoint.y / intrinsics.FocalLength.y * nearFar.x;
            float top = (resolution.y - intrinsics.PrincipalPoint.y) /
                        intrinsics.FocalLength.y * nearFar.x;
            Matrix4x4 projection = Matrix4x4.Frustum(left, right, bottom, top,
                nearFar.x, rasterFar);
            Matrix4x4 graphicsFromOptical = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(projection, true);
            return gpuProjection * graphicsFromOptical * opticalFromWorld;
        }
    }
}
