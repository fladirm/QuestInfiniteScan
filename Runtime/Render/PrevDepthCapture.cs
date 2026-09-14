using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FinalScan.Render
{
    /// <summary>Stereo view/projection of one frame in the ABI 2 convention (column-major, GPU projection). See HostMath.</summary>
    public sealed class StereoView
    {
        public readonly float[] ViewL = new float[16], ProjL = new float[16], ViewR = new float[16], ProjR = new float[16];
        public readonly float[] HeadPos = new float[3];
        public int FrameIndex;
        public bool Stereo;

        public void Set(in Matrix4x4 viewL, in Matrix4x4 gpuProjL, in Matrix4x4 viewR, in Matrix4x4 gpuProjR, in Vector3 headPos, int frame, bool stereo)
        {
            HostMath.ToColumnMajor(viewL, ViewL); HostMath.ToColumnMajor(gpuProjL, ProjL);
            HostMath.ToColumnMajor(viewR, ViewR); HostMath.ToColumnMajor(gpuProjR, ProjR);
            HostMath.ToArray(headPos, HeadPos);
            FrameIndex = frame; Stereo = stereo;
        }

        public void CopyFrom(StereoView o)
        {
            Array.Copy(o.ViewL, ViewL, 16); Array.Copy(o.ProjL, ProjL, 16); Array.Copy(o.ViewR, ViewR, 16); Array.Copy(o.ProjR, ProjR, 16);
            Array.Copy(o.HeadPos, HeadPos, 3); FrameIndex = o.FrameIndex; Stereo = o.Stereo;
        }
    }

    /// <summary>
    /// Owned previous-frame depth for the §13.5 occlusion prior: two ping-pong R32F texture arrays (2 layers under SPI).
    /// Frame N copies the resolved camera depth (opaques + surfels) into slot N&amp;1; at FRAME_BEGIN of frame N+1 the host
    /// hands slot N&amp;1 with frame N's view/projection to FsRender_SetPrevDepth while frame N+1 writes the other slot,
    /// so the native reader and the Unity writer never touch the same image in one frame. Imported once per handle.
    /// </summary>
    public sealed class PrevDepthCapture : IDisposable
    {
        sealed class Slot
        {
            public RenderTexture Rt;
            public RTHandle Handle;
            public readonly StereoView View = new StereoView();
            public bool Valid;
        }

        readonly Slot[] _slots = { new Slot(), new Slot() };
        int _write;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Layers { get; private set; }
        public int Reallocations { get; private set; }

        /// <summary>(Re)allocates both slots when the eye-buffer size or layer count changes. Returns false when allocation failed.</summary>
        public bool Ensure(int width, int height, int layers)
        {
            layers = Mathf.Clamp(layers, 1, 2);
            if (width <= 0 || height <= 0) return false;
            if (_slots[0].Rt != null && Width == width && Height == height && Layers == layers) return true;
            Release();
            Width = width; Height = height; Layers = layers;
            for (int i = 0; i < 2; i++)
            {
                var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R32_SFloat, 0)
                {
                    dimension = layers > 1 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D,
                    volumeDepth = layers,
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    sRGB = false,
                };
                var rt = new RenderTexture(desc) { name = "FS-PrevDepth" + i, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!rt.Create()) { Debug.LogError("[FinalScan] PrevDepth RT create failed"); Release(); return false; }
                _slots[i].Rt = rt;
                _slots[i].Handle = RTHandles.Alloc(rt);
                _slots[i].Valid = false;
            }
            Reallocations++;
            return true;
        }

        public bool IsAllocated => _slots[0].Rt != null;

        /// <summary>Slot the current frame renders into.</summary>
        public RTHandle WriteHandle => _slots[_write].Handle;

        /// <summary>Marks the write slot as containing the depth of <paramref name="view"/>'s frame and flips the ping-pong.</summary>
        public void Commit(StereoView view)
        {
            Slot s = _slots[_write];
            s.View.CopyFrom(view);
            s.Valid = true;
            _write ^= 1;
        }

        /// <summary>Latest committed slot (the previous frame from the point of view of the next FRAME_BEGIN).</summary>
        public bool TryGetLatest(out RenderTexture rt, out StereoView view)
        {
            Slot s = _slots[_write ^ 1];
            rt = s.Rt; view = s.View;
            return s.Valid && rt != null && rt.IsCreated();
        }

        public void Release()
        {
            foreach (Slot s in _slots)
            {
                s.Handle?.Release(); s.Handle = null;
                if (s.Rt != null) { s.Rt.Release(); UnityEngine.Object.Destroy(s.Rt); s.Rt = null; }
                s.Valid = false;
            }
        }

        public void Dispose() => Release();
    }
}
