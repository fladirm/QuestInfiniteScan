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
            internal Camera Camera;
        }

        // The cull runs in its own pass at BeforeRendering. Its native
        // cull-count reset records a transfer and is refused inside a render
        // pass, and the Vulkan event precondition cannot fix that here:
        // EnsureOutside is documented as undefined together with the SRP
        // RenderPass API that the render graph uses, and on device it changed
        // nothing. Being outside a render pass has to be structural.
        private sealed class MerkabaCullPass : ScriptableRenderPass
        {
            private MerkabaGridRenderer _renderer;

            internal MerkabaCullPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRendering;
            }

            internal void Setup(MerkabaGridRenderer renderer) =>
                _renderer = renderer;

            public override void RecordRenderGraph(RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_renderer == null) return;
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                using var cull = renderGraph.AddUnsafePass<PassData>(
                    "Merkaba Flower current-view cull", out PassData cullData);
                cullData.Renderer = _renderer;
                cullData.Camera = cameraData.camera;
                // Deliberately no attachment dependency. The cull writes only
                // the indirect argument buffer; declaring the active colour
                // target tied this pass to the render pass that owns it. The
                // pass survives because culling is disabled, not because it
                // touches a texture.
                cull.AllowPassCulling(false);
                cull.AllowGlobalStateModification(true);
                cull.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    data.Renderer.RecordViewCull(
                        CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), data.Camera));
            }
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
                    "Merkaba Flower indexed readout", out PassData passData);
                passData.Renderer = _renderer;
                builder.SetRenderAttachment(resources.activeColorTexture, 0,
                    AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture,
                    AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data,
                    RasterGraphContext context) =>
                    data.Renderer.RecordRenderPass(context.cmd));
            }
        }

        private MerkabaCullPass _cullPass;
        private MerkabaPass _pass;

        public override void Create()
        {
            _cullPass = new MerkabaCullPass();
            _pass = new MerkabaPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (_pass == null || _cullPass == null ||
                !MerkabaGridRenderer.TryGetActive(camera, out var gridRenderer))
                return;
            _cullPass.Setup(gridRenderer);
            _pass.Setup(gridRenderer);
            renderer.EnqueuePass(_cullPass);
            renderer.EnqueuePass(_pass);
        }
    }
}
