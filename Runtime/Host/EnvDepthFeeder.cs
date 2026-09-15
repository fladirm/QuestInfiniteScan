using System;
using FinalScan.Platform.Native;
using FinalScan.Platform.Sensor;
using FinalScan.Render;
using UnityEngine;

namespace FinalScan.Host
{
    /// <summary>
    /// Feeds every NEW Environment Depth frame (contract §6, §13.5) to the native side: FsRender_SetEnvDepth (occlusion
    /// prior of the cull) and FsMeas_SetEnvDepth (measurement front-end), same handle, same poses/fovs/near/far/XrTime.
    /// The single owner of the depth subscription is <see cref="SensorAuthority"/> (donor lesson: a second acquire per
    /// frame ends in XR_ERROR_LIMIT_REACHED); this class only polls its <see cref="SensorAuthority.LatestDepth"/> (live or
    /// replay) and forwards frames whose id advanced. Frames are deduplicated by XrTime (25 Hz sensor behind the 72 Hz
    /// callback, C01 evidence).
    ///
    /// ABI mapping: poses → 4x4 world-from-eye TRS matrices (column-major); fovs → [tan left, tan right, tan up, tan down]
    /// with provider signs; near/far as reported (far may be 0/inf = unbounded); texture = the owned 320x320x2 copy.
    /// </summary>
    public sealed class EnvDepthFeeder
    {
        readonly FinalScanHost _host;
        readonly SensorAuthority _authority;
        readonly DepthFrameDedupe _dedupe = new DepthFrameDedupe();
        readonly float[] _poseL = new float[16], _poseR = new float[16], _fovL = new float[4], _fovR = new float[4];
        long _lastFrameId = -1;
        int _lastRc, _lastMeasRc;

        public EnvDepthFeeder(FinalScanHost host, SensorAuthority authority) { _host = host; _authority = authority; }

        public string Source => _authority == null ? "none" : _authority.Replayer != null ? "replay" : "SensorAuthority";
        public int Accepted => _dedupe.Accepted;
        public int Repeats => _dedupe.Repeats;
        public int LastResult => _lastRc;
        public int LastMeasResult => _lastMeasRc;
        public long Pushed { get; private set; }
        public long PausedFrames { get; private set; }
        /// <summary>Depth frames refused as geometry evidence by the angular-velocity gate (contract §4.5; still fed to the HZB).</summary>
        public long GatedFrames { get; private set; }
        public float LastAngularDegPerSec { get; private set; }
        public long LastTimestampNs => _dedupe.LastTimestampNs;

        /// <summary>Main thread, once per host frame: forwards the authority's newest depth frame when it changed.</summary>
        public void Update()
        {
            if (_authority == null || !_host.NativeAvailable) return;
            DepthFrame d = _authority.LatestDepth;
            if (d == null || d.frameId == _lastFrameId || d.texture == null || !d.posesValid || !d.fovsValid) return;
            _lastFrameId = d.frameId;
            if (!_dedupe.Accept(d.xrTimeNs != 0, d.xrTimeNs)) return;
            if (!FinalScanHost.ScanEnabled) { PausedFrames++; return; }   // STOP SCAN: no new measurement priors
            IntPtr ptr = d.texture.GetNativeTexturePtr();
            if (ptr == IntPtr.Zero) return;
            HostMath.ToColumnMajor(d.poseMatrices[0], _poseL);
            HostMath.ToColumnMajor(d.poseMatrices[1], _poseR);
            Fill(d.fovTangents[0], _fovL); Fill(d.fovTangents[1], _fovR);
            float far = d.far;
            if (float.IsInfinity(far) || float.IsNaN(far)) far = 0f;
            float near = d.planesValid && d.near > 0f ? d.near : 0.1f;
            _lastRc = FinalScanHostNative.SetEnvDepth(ptr, (uint)d.width, (uint)d.height, _poseL, _poseR, _fovL, _fovR, near, far, d.xrTimeNs);
            // Motion gate (§4.5): the head angular speed around the depth capture time decides whether the frame is geometry
            // evidence. Environment Depth is computed over an exposure window; during a fast turn its surfaces land offset
            // from the canonical world (run 00:12: matched fell from 97 % to 68 % on a turn, +8 k surfels "in the air").
            var pipeline = _authority.Pipeline;
            if (pipeline != null && pipeline.Poses != null)
            {
                var verdict = pipeline.Gate.Evaluate(pipeline.Poses, d.xrTimeNs - MotionGate.WindowPadNs, d.xrTimeNs + MotionGate.WindowPadNs, out float ang, out _);
                LastAngularDegPerSec = ang;
                if (verdict == MotionVerdict.NotGeometryEvidence) { GatedFrames++; return; }
            }
            _lastMeasRc = FinalScanHostNative.SetMeasEnvDepth(ptr, (uint)d.width, (uint)d.height, _poseL, _poseR, _fovL, _fovR, near, far, d.xrTimeNs);
            Pushed++;
        }

        static void Fill(Vector4 v, float[] dst) { dst[0] = v.x; dst[1] = v.y; dst[2] = v.z; dst[3] = v.w; }
    }
}
