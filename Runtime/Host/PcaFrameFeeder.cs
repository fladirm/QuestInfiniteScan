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

        public long PairsPushed { get; private set; }
        public int LastPairResult { get; private set; }
        uint _lastPairId;
        /// <summary>A committed pair keeps the camera slots this long after its later capture (2 x the 30 Hz period + settle).</summary>
        public const long PairHoldNs = 100_000_000;

        /// <summary>Main thread, once per host frame. C10: a newly committed StereoPairer observation with live textures is pushed as
        /// BOTH camera slots plus FsMeas_SetStereoPair (capture-timestamp pairing); otherwise the newest frame of each eye is pushed
        /// for the appearance sample only (no stereo: the slots do not match a committed pair).</summary>
        public void Update()
        {
            if (_authority == null || _authority.Pipeline == null || !_host.NativeAvailable) return;
            if (!FinalScanHost.ScanEnabled) return;
            StereoObservation pair = _authority.Pipeline.Pairer.Latest;
            if (pair != null && pair.observationId != _lastPairId && pair.HasTextures && pair.skew != SkewClass.Reject)
            {
                if (Push(0, pair.leaseL) && Push(1, pair.leaseR))
                {
                    _lastPairId = pair.observationId;
                    // C10R motion authority: the pairer's geometry verdict, confidence, blur and timestamp uncertainty; native fails closed
                    LastPairResult = FinalScanHostNative.SetStereoPair(pair.observationId, pair.xrTimeNsL, pair.xrTimeNsR, (uint)pair.skew, pair.geometryEvidence, pair.confidence, pair.blurPenalty, Math.Max(pair.uncertaintyNsL, pair.uncertaintyNsR));
                    if (LastPairResult == FinalScanHostNative.ResultOk) PairsPushed++;
                }
                return;
            }
            // while the pairer is producing (a committed pair within PairHoldNs of the newest frame) the slots keep the pair:
            // an unpaired newer frame would break the pair the next depth frame solves with
            for (int e = 0; e < 2; e++)
            {
                PcaFrameRing ring = e == 0 ? _authority.Pipeline.RingL : _authority.Pipeline.RingR;
                CameraFrameLease newest = ring.Newest;
                if (newest != null && pair != null && newest.xrTimeNs - pair.createdXrTimeNs < PairHoldNs) continue;
                Push(e, newest);
            }
        }

        bool Push(int e, CameraFrameLease lease)
        {
            if (lease == null || lease.frameId == _lastFrameId[e] || lease.texture == null || !lease.poseValid || !lease.intrinsics.valid) return lease != null && lease.frameId == _lastFrameId[e];
            IntPtr ptr = lease.texture.GetNativeTexturePtr();
            if (ptr == IntPtr.Zero) return false;
            _lastFrameId[e] = lease.frameId;
            HostMath.ToColumnMajor(Matrix4x4.TRS(lease.cameraPose.position, lease.cameraPose.rotation, Vector3.one), _m);
            var k = lease.intrinsics;
            LastResult = FinalScanHostNative.SetCameraFrame(e, ptr, (uint)lease.texture.width, (uint)lease.texture.height, (uint)k.sensorWidth, (uint)k.sensorHeight, _m, k.fx, k.fy, k.cx, k.cy, _authority.PcaCopyUsesBlit, lease.xrTimeNs);
            if (LastResult == FinalScanHostNative.ResultOk) { Pushed++; LastXrTimeNs = lease.xrTimeNs; return true; }
            return false;
        }
    }
}
