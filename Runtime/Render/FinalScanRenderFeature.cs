using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FinalScan.Render
{
    /// <summary>Main-thread callbacks the render feature makes into the host (implemented by FinalScan.Host.FinalScanHost).</summary>
    public interface IRenderFrameHost
    {
        /// <summary>FsHost_GetRenderEventFunc() or IntPtr.Zero when the plugin is absent.</summary>
        IntPtr RenderEventFunc { get; }
        /// <summary>Called while recording FrameBeginPass, before the FS_HEVT_FRAME_BEGIN command is executed: FsRender_SetView + FsRender_SetPrevDepth.</summary>
        void OnFrameView(StereoView view, PrevDepthCapture prevDepth);
    }

    /// <summary>
    /// URP RenderGraph integration of contract §13.1. Four passes per camera frame, in this order:
    ///
    ///   1. FrameBeginPass   BeforeRenderingOpaques (250)   unsafe pass: IssuePluginEvent(FS_HEVT_FRAME_BEGIN=100)
    ///   2. SurfelDrawPass   AfterRenderingOpaques  (300)   raster pass on the active color+depth attachment (depth write on):
    ///                                                      the indirect draws of SurfelRenderer.Draw (opaque bucket, aggregate bucket)
    ///   3. PrevDepthCopyPass AfterRenderingOpaques+1 (301) raster pass, ConfigureInput(Depth): blit _CameraDepthTexture → owned R32F array
    ///   4. FrameEndPass     AfterRendering (1000)          unsafe pass: IssuePluginEvent(FS_HEVT_FRAME_END=101)
    ///
    /// Ordering guarantee. URP records injected passes strictly by RenderPassEvent (ScriptableRenderer sorts the pass
    /// list and RecordCustomRenderGraphPasses walks event ranges in increasing order); the RenderGraph compiler keeps the
    /// recording order of graphics passes (only async-compute passes may move) and culling is disabled on every pass
    /// here (AllowPassCulling(false)), so the compiled command stream contains FRAME_BEGIN, then the opaque renderer
    /// lists, then the indirect draws, then the copy, then FRAME_END. Unity executes plugin events on the render thread
    /// at the position they hold in the command stream, i.e. FS_HEVT_FRAME_BEGIN retires fences and publishes the cull
    /// of this frame before the draws that consume its indirect args are translated to Vulkan. Because
    /// FsRender_SetView is called from FrameBeginPass.RecordRenderGraph (main thread, before the render thread reaches
    /// this frame's commands), the cull always has this frame's stereo view.
    ///
    /// Depth-copy placement. URP splits the AfterRenderingOpaques event around its own CopyDepth pass at the earliest
    /// event of any pass that requested ScriptableRenderPassInput.Depth (UniversalRenderer.RecordCustomPassesWithDepthCopyAndMotion):
    /// passes with a lower event run before the copy, the rest after. The surfel draw (300) therefore lands before the
    /// copy and PrevDepthCopyPass (301, the only depth requester) after it, so _CameraDepthTexture contains opaques AND
    /// surfels when it is captured as next frame's occlusion prior.
    ///
    /// XR: one FRAME_BEGIN/FRAME_END per frame (guarded with xr.isFirstCameraPass for multipass); on Quest the renderer
    /// runs single-pass instanced so there is exactly one camera pass.
    /// </summary>
    public sealed class FinalScanRenderFeature : ScriptableRendererFeature
    {
        public const string DepthCopyShaderName = "FinalScan/DepthCopy";

        [Tooltip("FinalScan/DepthCopy (assigned by the editor setup so it ships in the build).")]
        [SerializeField] Shader depthCopyShader;
        [Tooltip("Capture the resolved camera depth after the surfel draw as the §13.5 previous-frame occlusion prior.")]
        [SerializeField] bool capturePrevDepth = true;
        [Tooltip("Also run in the editor Game view without XR (debug only; native is absent there).")]
        [SerializeField] bool runWithoutNative;

        /// <summary>Set by FinalScanHost.OnEnable.</summary>
        public static IRenderFrameHost Host { get; set; }

        FrameBeginPass _begin;
        SurfelDrawPass _draw;
        PrevDepthCopyPass _copy;
        FrameEndPass _end;
        Material _copyMaterial;
        readonly PrevDepthCapture _prevDepth = new PrevDepthCapture();
        readonly StereoView _view = new StereoView();

        public PrevDepthCapture PrevDepth => _prevDepth;

        public override void Create()
        {
            _begin = new FrameBeginPass(this) { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
            _draw = new SurfelDrawPass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
            _copy = new PrevDepthCopyPass(this) { renderPassEvent = RenderPassEvent.AfterRenderingOpaques + 1 };
            _copy.ConfigureInput(ScriptableRenderPassInput.Depth);
            _end = new FrameEndPass { renderPassEvent = RenderPassEvent.AfterRendering };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            IRenderFrameHost host = Host;
            bool native = host != null && host.RenderEventFunc != IntPtr.Zero;
            if (!native && !runWithoutNative) return;

            if (native) renderer.EnqueuePass(_begin);
            if (SurfelRenderer.Current != null && SurfelRenderer.Current.BuffersReady) renderer.EnqueuePass(_draw);
            if (capturePrevDepth && EnsureCopyMaterial()) renderer.EnqueuePass(_copy);
            if (native) renderer.EnqueuePass(_end);
        }

        bool EnsureCopyMaterial()
        {
            if (_copyMaterial != null) return true;
            Shader s = depthCopyShader != null ? depthCopyShader : Shader.Find(DepthCopyShaderName);
            if (s == null) return false;
            _copyMaterial = new Material(s) { name = "FS-DepthCopy", hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            _prevDepth.Release();
            if (_copyMaterial != null) { CoreUtils.Destroy(_copyMaterial); _copyMaterial = null; }
        }

        // ------------------------------------------------------------------ view capture

        /// <summary>Stereo view/proj of this camera pass in ABI convention (see HostMath); falls back to mono when XR is off.</summary>
        static void CaptureView(UniversalCameraData cam, StereoView into)
        {
            Matrix4x4 viewL, projL, viewR, projR;
            bool stereo = cam.xr != null && cam.xr.enabled;
            if (stereo)
            {
                viewL = cam.xr.GetViewMatrix(0); projL = cam.xr.GetProjMatrix(0);
                if (cam.xr.viewCount > 1) { viewR = cam.xr.GetViewMatrix(1); projR = cam.xr.GetProjMatrix(1); }
                else { viewR = viewL; projR = projL; }
            }
            else
            {
                viewL = viewR = cam.camera.worldToCameraMatrix;
                projL = projR = cam.camera.projectionMatrix;
            }
            into.Set(viewL, GL.GetGPUProjectionMatrix(projL, true), viewR, GL.GetGPUProjectionMatrix(projR, true),
                     cam.camera.transform.position, Time.frameCount, stereo);
        }

        // ------------------------------------------------------------------ passes

        sealed class PluginEventData { public IntPtr Func; public int EventId; }

        static void RecordPluginEvent(RenderGraph rg, string name, IntPtr func, int eventId)
        {
            using (IUnsafeRenderGraphBuilder b = rg.AddUnsafePass(name, out PluginEventData d))
            {
                d.Func = func; d.EventId = eventId;
                b.AllowPassCulling(false);
                b.AllowGlobalStateModification(true);
                b.SetRenderFunc((PluginEventData data, UnsafeGraphContext ctx) => ctx.cmd.IssuePluginEvent(data.Func, data.EventId));
            }
        }

        sealed class FrameBeginPass : ScriptableRenderPass
        {
            readonly FinalScanRenderFeature _f;
            public FrameBeginPass(FinalScanRenderFeature f) { _f = f; profilingSampler = new ProfilingSampler("FS FrameBegin"); }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                IRenderFrameHost host = Host;
                if (host == null) return;
                UniversalCameraData cam = frameData.Get<UniversalCameraData>();
                if (cam.xr != null && cam.xr.enabled && !cam.xr.isFirstCameraPass) return;
                CaptureView(cam, _f._view);
                host.OnFrameView(_f._view, _f._prevDepth);          // FsRender_SetView + FsRender_SetPrevDepth (main thread)
                IntPtr func = host.RenderEventFunc;
                if (func != IntPtr.Zero) RecordPluginEvent(rg, "FS FrameBegin", func, 100 /* FS_HEVT_FRAME_BEGIN */);
            }
        }

        sealed class FrameEndPass : ScriptableRenderPass
        {
            public FrameEndPass() { profilingSampler = new ProfilingSampler("FS FrameEnd"); }
            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                IRenderFrameHost host = Host;
                if (host == null) return;
                UniversalCameraData cam = frameData.Get<UniversalCameraData>();
                if (cam.xr != null && cam.xr.enabled && !cam.xr.isFirstCameraPass) return;
                IntPtr func = host.RenderEventFunc;
                if (func != IntPtr.Zero) RecordPluginEvent(rg, "FS FrameEnd", func, 101 /* FS_HEVT_FRAME_END */);
            }
        }

        sealed class SurfelDrawData { public SurfelRenderer Renderer; }

        sealed class SurfelDrawPass : ScriptableRenderPass
        {
            public SurfelDrawPass() { profilingSampler = new ProfilingSampler("FS SurfelDraw"); }
            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                SurfelRenderer r = SurfelRenderer.Current;
                if (r == null || !r.BuffersReady) return;
                UniversalResourceData res = frameData.Get<UniversalResourceData>();
                if (!res.activeColorTexture.IsValid() || !res.activeDepthTexture.IsValid()) return;
                using (IRasterRenderGraphBuilder b = rg.AddRasterRenderPass("FS SurfelDraw", out SurfelDrawData d, profilingSampler))
                {
                    d.Renderer = r;
                    b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.ReadWrite);
                    b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.ReadWrite);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((SurfelDrawData data, RasterGraphContext ctx) => data.Renderer.Draw(ctx.cmd));
                }
            }
        }

        sealed class DepthCopyData { public TextureHandle Source; public Material Material; }

        sealed class PrevDepthCopyPass : ScriptableRenderPass
        {
            readonly FinalScanRenderFeature _f;
            static readonly Vector4 FullScaleBias = new Vector4(1f, 1f, 0f, 0f);
            public PrevDepthCopyPass(FinalScanRenderFeature f) { _f = f; profilingSampler = new ProfilingSampler("FS PrevDepthCopy"); }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                UniversalResourceData res = frameData.Get<UniversalResourceData>();
                UniversalCameraData cam = frameData.Get<UniversalCameraData>();
                if (!res.cameraDepthTexture.IsValid() || _f._copyMaterial == null) return;
                if (cam.xr != null && cam.xr.enabled && !cam.xr.isFirstCameraPass) return;
                TextureDesc src = rg.GetTextureDesc(res.cameraDepthTexture);
                int layers = src.dimension == TextureDimension.Tex2DArray ? Mathf.Max(1, src.slices) : 1;
                if (!_f._prevDepth.Ensure(src.width, src.height, layers)) return;
                if (_f._view.FrameIndex != Time.frameCount) CaptureView(cam, _f._view);   // FrameBeginPass not enqueued (no native)
                TextureHandle dst = rg.ImportTexture(_f._prevDepth.WriteHandle);
                using (IRasterRenderGraphBuilder b = rg.AddRasterRenderPass("FS PrevDepthCopy", out DepthCopyData d, profilingSampler))
                {
                    d.Source = res.cameraDepthTexture;
                    d.Material = _f._copyMaterial;
                    b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                    b.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((DepthCopyData data, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, (RTHandle)data.Source, FullScaleBias, data.Material, 0));
                }
                // The view captured by FrameBeginPass this frame is what the copied depth was rendered with.
                _f._prevDepth.Commit(_f._view);
            }
        }
    }
}
