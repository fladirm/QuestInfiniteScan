using System;
using FinalScan.Platform.Native;
using FinalScan.Platform.Sensor;
using FinalScan.Render;
using UnityEngine;

namespace FinalScan.Host
{
    /// <summary>
    /// C16a (contract §14, keyframe authority): forwards the newest PCA frame lease of each eye to the native measurement
    /// front-end (FsMeas_SetCameraFrame): owned texture copy, world-from-camera pose (head pose at the capture time through
    /// the lens offset, §4.3/§4.6), pinhole intrinsics in delivered pixels, capture XrTime. The emit kernel samples the colour
    /// of every measurement whose depth time is within FS_MEAS_CAM_MAX_AGE_NS of the frame; fusion blends it into the
    /// surfel appearance. The per-eye ring (depth 8, ~250 ms at 30 Hz) keeps the texture slot alive well past the job.
    /// </summary>
    public sealed class PcaFrameFeeder
    {
        readonly FinalScanHost _host;
        readonly SensorAuthority _authority;
        readonly float[] _m = new float[16];
        readonly long[] _lastFrameId = { -1, -1 };
        public long Pushed { get; private set; }
        public int LastResult { get; private set; }
        public long LastXrTimeNs { get; private set; }

        public PcaFrameFeeder(FinalScanHost host, SensorAuthority authority) { _host = host; _authority = authority; }

        /// <summary>Main thread, once per host frame.</summary>
        public void Update()
        {
            if (_authority == null || _authority.Pipeline == null || !_host.NativeAvailable) return;
            if (!FinalScanHost.ScanEnabled) return;
            for (int e = 0; e < 2; e++)
            {
                PcaFrameRing ring = e == 0 ? _authority.Pipeline.RingL : _authority.Pipeline.RingR;
                CameraFrameLease lease = ring.Newest;
                if (lease == null || lease.frameId == _lastFrameId[e] || lease.texture == null || !lease.poseValid || !lease.intrinsics.valid) continue;
                _lastFrameId[e] = lease.frameId;
                IntPtr ptr = lease.texture.GetNativeTexturePtr();
                if (ptr == IntPtr.Zero) continue;
                HostMath.ToColumnMajor(Matrix4x4.TRS(lease.cameraPose.position, lease.cameraPose.rotation, Vector3.one), _m);
                var k = lease.intrinsics;
                LastResult = FinalScanHostNative.SetCameraFrame(e, ptr, (uint)lease.texture.width, (uint)lease.texture.height, (uint)k.sensorWidth, (uint)k.sensorHeight, _m, k.fx, k.fy, k.cx, k.cy, _authority.PcaCopyUsesBlit, lease.xrTimeNs);
                if (LastResult == FinalScanHostNative.ResultOk) { Pushed++; LastXrTimeNs = lease.xrTimeNs; }
            }
        }
    }
}
