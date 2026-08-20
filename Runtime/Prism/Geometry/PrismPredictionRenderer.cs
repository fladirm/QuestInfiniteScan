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
        [SerializeField, Range(3, 12)] private int targetRingSlots = 4;

        private static readonly int VerticesId = Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId = Shader.PropertyToID("_ContactIndices");
        private static readonly int ClipFromChunkId = Shader.PropertyToID("_ClipFromChunk");
        private static readonly int OpticalFromChunkId = Shader.PropertyToID("_OpticalFromChunk");

        private readonly RenderTargetIdentifier[] _mrt = new RenderTargetIdentifier[4];
        private PredictionTargetRing _targets;
        private PredictionFrameLease _latest;
        private Material _material;
        private MaterialPropertyBlock _properties;
        private ContactMeshletBuffers _meshlets;
        private bool _rendering;
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

        public void StartRendering(PrismDepthPreprocessor source = null)
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
            if (predictionShader == null)
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
            depthPreprocessor.FrameReady += OnDepthFrame;
            _rendering = true;
        }

        public void StopRendering()
        {
            if (_rendering && depthPreprocessor != null)
                depthPreprocessor.FrameReady -= OnDepthFrame;
            _rendering = false;
            _latest?.Dispose();
            _latest = null;
            _targets?.Dispose();
            _targets = null;
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

        private void OnDepthFrame(NormalizedRigFrameLease source)
        {
            if (!_rendering || source == null || !source.IsValid) return;
            if (!_targets.TryBegin(source, out PredictionFrameLease prediction))
            {
                _backpressure++;
                return;
            }

            CommandBuffer command = CommandBufferPool.Get("Cone-PRISM Predict ContactFilm");
            try
            {
                _mrt[0] = new RenderTargetIdentifier(prediction.DepthSigma);
                _mrt[1] = new RenderTargetIdentifier(prediction.NormalConfidence);
                _mrt[2] = new RenderTargetIdentifier(prediction.FilmIdGeneration);
                _mrt[3] = new RenderTargetIdentifier(prediction.UvMetadata);
                var depthTarget = new RenderTargetIdentifier(prediction.HardwareDepth);

                BindMeshlets();
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

                    command.SetRenderTarget(_mrt, depthTarget, 0,
                        CubemapFace.Unknown, eye);
                    command.ClearRenderTarget(true, true, Color.clear, 1f);
                    command.DrawProceduralIndirect(Matrix4x4.identity, _material, 0,
                        MeshTopology.Triangles, _meshlets.DrawArguments, 0, _properties);
                }
                Graphics.ExecuteCommandBuffer(command);
                prediction.CommitGpuWrite();

                PredictionFrameLease previous = _latest;
                _latest = prediction;
                previous?.Dispose();
                _rendered++;
                PredictionReady?.Invoke(prediction);
            }
            catch (Exception exception)
            {
                prediction.Dispose();
                Logger.Error($"Cone-PRISM prediction raster failed: {exception.Message}");
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
            _properties.SetBuffer(IndicesId, _meshlets.Indices);
        }

        private static Matrix4x4 BuildClipFromWorld(GpuImageView view,
            Matrix4x4 opticalFromWorld)
        {
            RigIntrinsics intrinsics = view.Intrinsics;
            Vector2Int resolution = intrinsics.ImageResolution;
            Vector2 nearFar = view.DepthNearFar;
            float left = -intrinsics.PrincipalPoint.x / intrinsics.FocalLength.x * nearFar.x;
            float right = (resolution.x - intrinsics.PrincipalPoint.x) /
                          intrinsics.FocalLength.x * nearFar.x;
            float bottom = -intrinsics.PrincipalPoint.y / intrinsics.FocalLength.y * nearFar.x;
            float top = (resolution.y - intrinsics.PrincipalPoint.y) /
                        intrinsics.FocalLength.y * nearFar.x;
            Matrix4x4 projection = Matrix4x4.Frustum(left, right, bottom, top,
                nearFar.x, nearFar.y);
            Matrix4x4 graphicsFromOptical = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(projection, true);
            return gpuProjection * graphicsFromOptical * opticalFromWorld;
        }
    }
}
