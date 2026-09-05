using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Genesis.RoomScan
{
    /// <summary>One URP raster pass for the cached Merkaba readout stream.</summary>
    public sealed class MerkabaRenderFeature : ScriptableRendererFeature
    {
        private sealed class PassData
        {
            internal MerkabaGridRenderer Renderer;
            internal int Slot;
            internal BufferHandle Vertices;
            internal BufferHandle Indices;
            internal DrawTimingState Timing;
        }

        private sealed class VisibilityPassData
        {
            internal MerkabaGridRenderer Renderer;
            internal int Slot;
            internal BufferHandle Vertices;
            internal BufferHandle Indices;
            internal Vector4[] GridCullPlanes;
            internal DrawTimingState Timing;
        }

        private sealed class DrawTimingState
        {
            internal bool Acquired;
        }

        private sealed class MerkabaPass : ScriptableRenderPass
        {
            private MerkabaGridRenderer _renderer;

            internal MerkabaPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            }

            internal void Setup(MerkabaGridRenderer renderer) =>
                _renderer = renderer;

            public override void RecordRenderGraph(RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_renderer == null) return;
                UniversalResourceData resources =
                    frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();
                if (!_renderer.TryGetFrontRenderResources(cameraData.camera,
                        out int slot, out GraphicsBuffer vertices,
                        out GraphicsBuffer indices,
                        out Vector4[] gridCullPlanes,
                        out bool compactVisibility))
                    return;
                BufferHandle vertexHandle = renderGraph.ImportBuffer(vertices);
                BufferHandle indexHandle = renderGraph.ImportBuffer(indices);
                var timing = new DrawTimingState();

                if (compactVisibility)
                {
                    using var visibilityBuilder =
                        renderGraph.AddComputePass<VisibilityPassData>(
                            "Merkaba M8 Visibility", out var visibilityData);
                    visibilityData.Renderer = _renderer;
                    visibilityData.Slot = slot;
                    visibilityData.Vertices = vertexHandle;
                    visibilityData.Indices = indexHandle;
                    visibilityData.GridCullPlanes = gridCullPlanes;
                    visibilityData.Timing = timing;
                    visibilityBuilder.UseBuffer(vertexHandle,
                        AccessFlags.Read);
                    visibilityBuilder.UseBuffer(indexHandle,
                        AccessFlags.Write);
                    visibilityBuilder.AllowPassCulling(false);
                    visibilityBuilder.SetRenderFunc(static (
                        VisibilityPassData data, ComputeGraphContext context) =>
                        data.Timing.Acquired =
                            data.Renderer.RecordVisibilityPass(context.cmd,
                            data.Slot, data.Vertices, data.Indices,
                            data.GridCullPlanes));
                }

                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Merkaba M8 Readout", out PassData passData);
                passData.Renderer = _renderer;
                passData.Slot = slot;
                passData.Vertices = vertexHandle;
                passData.Indices = indexHandle;
                passData.Timing = timing;
                builder.UseBuffer(vertexHandle, AccessFlags.Read);
                builder.UseBuffer(indexHandle, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0,
                    AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture,
                    AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data,
                    RasterGraphContext context) =>
                    data.Renderer.RecordRenderPass(context.cmd, data.Slot,
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
                !MerkabaGridRenderer.TryGetActive(camera, out var gridRenderer))
                return;
            _pass.Setup(gridRenderer);
            renderer.EnqueuePass(_pass);
        }
    }
}
