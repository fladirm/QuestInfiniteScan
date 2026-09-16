using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Genesis.RoomScan
{
    /// <summary>
    /// One tile-first visibility compute pass and one raster draw of the
    /// published Merkaba readout page pool.
    /// </summary>
    public sealed class MerkabaRenderFeature : ScriptableRendererFeature
    {
        private sealed class PassData
        {
            internal MerkabaGridRenderer Renderer;
            internal BufferHandle Indices;
            internal DrawTimingState Timing;
        }

        private sealed class VisibilityPassData
        {
            internal MerkabaGridRenderer Renderer;
            internal ComputeBuffer FrontIndex;
            internal int FrontTileCount;
            internal Vector4[] GridCullPlanes;
            internal Vector3 CameraWorld;
            internal GraphicsBuffer IndexBuffer;
            internal BufferHandle Indices;
            internal DrawTimingState Timing;
        }

        private sealed class DrawTimingState
        {
            internal bool Acquired;
        }

        private sealed class MerkabaPass : ScriptableRenderPass
        {
            private MerkabaGridRenderer _renderer;
            private GraphicsBuffer _indices;

            internal MerkabaPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            }

            internal void Setup(MerkabaGridRenderer renderer,
                GraphicsBuffer indices)
            {
                _renderer = renderer;
                _indices = indices;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_renderer == null || _indices == null) return;
                UniversalResourceData resources =
                    frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();
                if (!_renderer.TryGetFrontRenderResources(cameraData.camera,
                        out ComputeBuffer frontIndex, out int frontTileCount,
                        out Vector4[] gridCullPlanes))
                    return;
                BufferHandle indexHandle = renderGraph.ImportBuffer(_indices);
                var timing = new DrawTimingState();

                using (var visibilityBuilder =
                       renderGraph.AddComputePass<VisibilityPassData>(
                           "Merkaba M8 Visibility", out var visibilityData))
                {
                    visibilityData.Renderer = _renderer;
                    visibilityData.FrontIndex = frontIndex;
                    visibilityData.FrontTileCount = frontTileCount;
                    visibilityData.GridCullPlanes = gridCullPlanes;
                    visibilityData.CameraWorld =
                        cameraData.camera.transform.position;
                    visibilityData.IndexBuffer = _indices;
                    visibilityData.Indices = indexHandle;
                    visibilityData.Timing = timing;
                    visibilityBuilder.UseBuffer(indexHandle,
                        AccessFlags.Write);
                    visibilityBuilder.AllowPassCulling(false);
                    visibilityBuilder.SetRenderFunc(static (
                        VisibilityPassData data, ComputeGraphContext context) =>
                        data.Timing.Acquired =
                            data.Renderer.RecordVisibilityPass(context.cmd,
                                data.FrontIndex, data.FrontTileCount,
                                data.GridCullPlanes, data.CameraWorld,
                                data.IndexBuffer));
                }

                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Merkaba M8 Readout", out PassData passData);
                passData.Renderer = _renderer;
                passData.Indices = indexHandle;
                passData.Timing = timing;
                builder.UseBuffer(indexHandle, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0,
                    AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture,
                    AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data,
                    RasterGraphContext context) =>
                    data.Renderer.RecordRenderPass(context.cmd,
                        data.Timing.Acquired));
            }
        }

        private MerkabaPass _pass;

        public override void Create() => _pass = new MerkabaPass();

        public override void AddRenderPasses(ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (_pass == null ||
                !MerkabaGridRenderer.TryGetActive(camera, out var gridRenderer) ||
                MerkabaGrid.Instance == null)
                return;
            _pass.Setup(gridRenderer, gridRenderer.FrontVisibleIndices);
            renderer.EnqueuePass(_pass);
        }
    }
}
