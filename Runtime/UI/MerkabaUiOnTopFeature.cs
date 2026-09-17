using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

namespace Genesis.RoomScan.UI
{
    /// <summary>
    /// The UX is always on top. Renderers of the UI layer (the world-space
    /// panel) are drawn once, after every world transparent, with the depth
    /// test forced to Always, inside the one base XR camera. No second
    /// camera, no camera stack, no intermediate target. The setup wizard
    /// removes the UI layer from the renderer's opaque/transparent masks so
    /// nothing draws it twice.
    /// </summary>
    public sealed class MerkabaUiOnTopFeature : ScriptableRendererFeature
    {
        private sealed class PassData
        {
            internal RendererListHandle List;
        }

        private sealed class UiOnTopPass : ScriptableRenderPass
        {
            private static readonly ShaderTagId[] Tags =
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForwardOnly")
            };

            private int _layerMask;

            internal UiOnTopPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            internal void Setup(int layerMask) => _layerMask = layerMask;

            public override void RecordRenderGraph(RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_layerMask == 0) return;
                UniversalResourceData resources =
                    frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData =
                    frameData.Get<UniversalRenderingData>();
                var description = new RendererListDesc(Tags,
                    renderingData.cullResults, cameraData.camera)
                {
                    renderQueueRange = RenderQueueRange.all,
                    layerMask = _layerMask,
                    sortingCriteria = SortingCriteria.CommonTransparent,
                    stateBlock = new RenderStateBlock(RenderStateMask.Depth)
                    {
                        depthState = new DepthState(false,
                            CompareFunction.Always)
                    }
                };
                RendererListHandle list =
                    renderGraph.CreateRendererList(description);
                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Merkaba UX On Top", out PassData data);
                data.List = list;
                builder.UseRendererList(list);
                builder.SetRenderAttachment(resources.activeColorTexture, 0,
                    AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture,
                    AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData,
                    RasterGraphContext context) =>
                    context.cmd.DrawRendererList(passData.List));
            }
        }

        [SerializeField] private LayerMask uiLayers = 1 << 5;

        private UiOnTopPass _pass;

        public override void Create() => _pass = new UiOnTopPass();

        public override void AddRenderPasses(ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (_pass == null ||
                renderingData.cameraData.cameraType != CameraType.Game)
                return;
            _pass.Setup(uiLayers.value);
            renderer.EnqueuePass(_pass);
        }
    }
}
