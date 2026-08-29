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
                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Merkaba M8 Readout", out PassData passData);
                passData.Renderer = _renderer;
                builder.SetRenderAttachment(resources.activeColorTexture, 0,
                    AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture,
                    AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data,
                    RasterGraphContext context) =>
                    data.Renderer.RecordRenderPass(context.cmd));
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
