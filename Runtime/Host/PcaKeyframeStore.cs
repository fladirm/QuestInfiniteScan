using System;
using FinalScan.Platform.Native;
using FinalScan.Platform.Sensor;
using FinalScan.Render;
using UnityEngine;

namespace FinalScan.Host
{
    /// <summary>
    /// C11 temporal multiview keyframes (contract §6): keeps up to <see cref="Slots"/> owned copies of LEFT PCA frames, a new one
    /// whenever the left camera moved at least <see cref="SpacingMeters"/> from the newest keyframe (or turned
    /// <see cref="SpacingDegrees"/>), oldest slot overwritten. Each copy is pushed with its located capture pose, intrinsics and
    /// capture XrTime through FsMeas_SetKeyframe; the native front-end selects per depth frame the keyframe whose baseline to the
    /// current left camera lies in 20..50 cm (closest to 30 cm) and solves the large-baseline match against it. The copy is a
    /// GPU CopyTexture of the owned lease (same format and row order as the camera slots).
    /// </summary>
    public sealed class PcaKeyframeStore
    {
        public const int Slots = 4;                 // twin: FS_MEAS_KEYFRAMES
        public const float SpacingMeters = 0.12f;   // 4 slots x 0.12 m spacing spans the 0.20..0.50 m baseline window
        public const float SpacingDegrees = 15f;

        readonly FinalScanHost _host;
        readonly SensorAuthority _authority;
        readonly RenderTexture[] _rt = new RenderTexture[Slots];
        readonly Vector3[] _pos = new Vector3[Slots];
        readonly Quaternion[] _rot = new Quaternion[Slots];
        readonly bool[] _used = new bool[Slots];
        readonly float[] _m = new float[16];
        int _newest = -1;
        long _lastFrameId = -1;
        public long Stored { get; private set; }
        public int LastResult { get; private set; }

        public PcaKeyframeStore(FinalScanHost host, SensorAuthority authority) { _host = host; _authority = authority; }

        /// <summary>Main thread, once per host frame.</summary>
        public void Update()
        {
            if (_authority == null || _authority.Pipeline == null || !_host.NativeAvailable || !FinalScanHost.ScanEnabled) return;
            CameraFrameLease lease = _authority.Pipeline.RingL.Newest;
            if (lease == null || lease.frameId == _lastFrameId || !lease.poseValid || !lease.intrinsics.valid || !(lease.texture is RenderTexture src)) return;
            _lastFrameId = lease.frameId;
            Vector3 p = lease.cameraPose.position; Quaternion q = lease.cameraPose.rotation;
            if (_newest >= 0 && Vector3.Distance(p, _pos[_newest]) < SpacingMeters && Quaternion.Angle(q, _rot[_newest]) < SpacingDegrees) return;
            int slot = (_newest + 1) % Slots;
            RenderTexture dst = _rt[slot];
            if (dst == null || dst.width != src.width || dst.height != src.height || dst.graphicsFormat != src.graphicsFormat)
            {
                if (dst != null) { dst.Release(); UnityEngine.Object.Destroy(dst); }
                dst = new RenderTexture(src.width, src.height, 0, src.graphicsFormat) { useMipMap = false, autoGenerateMips = false, name = "FS pca keyframe " + slot };
                dst.Create();
                _rt[slot] = dst;
            }
            Graphics.CopyTexture(src, dst);
            IntPtr ptr = dst.GetNativeTexturePtr();
            if (ptr == IntPtr.Zero) return;
            HostMath.ToColumnMajor(Matrix4x4.TRS(p, q, Vector3.one), _m);
            var k = lease.intrinsics;
            LastResult = FinalScanHostNative.SetKeyframe((uint)slot, ptr, (uint)dst.width, (uint)dst.height, (uint)k.sensorWidth, (uint)k.sensorHeight, _m, k.fx, k.fy, k.cx, k.cy, _authority.PcaCopyUsesBlit, lease.xrTimeNs);
            if (LastResult != FinalScanHostNative.ResultOk) return;
            _pos[slot] = p; _rot[slot] = q; _used[slot] = true; _newest = slot; Stored++;
        }

        public void Dispose()
        {
            for (int i = 0; i < Slots; i++)
            {
                if (_rt[i] == null) continue;
                if (_used[i] && _host.NativeAvailable) FinalScanHostNative.SetKeyframe((uint)i, IntPtr.Zero, 0, 0, 0, 0, _m, 0, 0, 0, 0, false, 0);
                _rt[i].Release(); UnityEngine.Object.Destroy(_rt[i]); _rt[i] = null; _used[i] = false;
            }
            _newest = -1;
        }
    }
}
