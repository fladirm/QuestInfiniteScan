using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// One Environment Depth frame (contract §6): own timestamp, per-eye pose and fov, near/far and an owned texture copy.
    /// It is a prior and free-space evidence with its own time base, never "depth attached to a PCA frame".
    /// Fovs are tangents (tan of the OpenXR half-angles: x=left (negative), y=right, z=up, w=down (negative)).
    /// </summary>
    public sealed class DepthFrame
    {
        public long frameId;
        public long xrTimeNs;
        public long receiveXrTimeNs;
        public long ageNs;
        /// <summary>Per-eye view poses (world) and the same as localToWorld matrices for the host (reflection reader).</summary>
        public readonly Pose[] poses = new Pose[2];
        public readonly Matrix4x4[] poseMatrices = new Matrix4x4[2];
        public readonly Vector4[] fovTangents = new Vector4[2];
        public float near, far;
        public bool posesValid, fovsValid, planesValid;
        /// <summary>Owned copy: Tex2DArray (2 slices) RenderTexture on device, RFloat (blit) or the source color format (copy); Texture2DArray in replay.</summary>
        public Texture texture;
        public int width, height;
        public int poolSlot = -1;
        int _refs; Action<DepthFrame> _onFree;
        public bool Alive => _refs > 0;
        internal void Begin(Action<DepthFrame> onFree) { _refs = 1; _onFree = onFree; }
        public DepthFrame Acquire() { if (_refs <= 0) throw new InvalidOperationException("depth frame freed"); _refs++; return this; }
        public void Release() { if (_refs <= 0) return; if (--_refs == 0) { var cb = _onFree; _onFree = null; cb?.Invoke(this); } }
    }

    /// <summary>
    /// Environment Depth ingest through ARFoundation's AROcclusionManager (Meta OpenXR occlusion subsystem, XR_META_environment_depth).
    /// Measured: 320x320x2 Tex2DArray, 25 Hz (XrTime delta 40 ms), the frameReceived callback fires at 72 Hz with repeated
    /// frames → dedupe by timestamp; age p50 56 ms; poses/fovs present; near 0.1, far +inf.
    /// Rules: exactly one AROcclusionManager (ARF acquires once per frame; a second acquire → XR_ERROR_LIMIT_REACHED on the donor),
    /// restart after resume (donor b34910b), hand removal enabled through the Meta subsystem when the API exists
    /// (Unity.XR.MetaOpenXR is not an asmdef reference → reflection on the subsystem type).
    /// </summary>
    public sealed class EnvDepthSource
    {
        public const int PoolSize = 4;
        public const int RestartDelayFrames = 2;

        AROcclusionManager _occ;
        bool _subscribed, _added, _sessionAdded;
        readonly RenderTexture[] _pool = new RenderTexture[PoolSize];
        readonly bool[] _free = new bool[PoolSize];
        readonly Func<double> _ovrNowSeconds;
        readonly Action<string> _log;
        Func<Pose, Pose> _toWorld;
        long _nextId = 1, _lastTs;
        int _restartAtFrame = -1;
        bool _handRemovalTried;
        bool _copyModeDecided, _copyByCopyTexture;
        readonly CadenceMeter _cadence = new CadenceMeter(40_000_000);

        public DepthFrame LatestDepth { get; private set; }
        public long Frames { get; private set; }
        public long Repeated { get; private set; }
        public long NonMonotonic { get; private set; }
        public long NoTimestamp { get; private set; }
        public long CopyErrors { get; private set; }
        public long PoolExhausted { get; private set; }
        public long Restarts { get; private set; }
        public long LastAgeNs { get; private set; }
        public double AgeMeanNs { get; private set; }
        public string HandRemoval { get; private set; } = "untried";
        public string CopyMode => !_copyModeDecided ? "undecided" : _copyByCopyTexture ? "CopyTexture" : "BlitPerSlice(RFloat)";
        public bool Running => _occ != null && _occ.enabled && _subscribed;
        public double Fps(long nowXrNs) => _cadence.DeliveredFps(nowXrNs);
        public event Action<DepthFrame> DepthReceived;

        public EnvDepthSource(Func<double> ovrNowSeconds, Func<Pose, Pose> toWorld, Action<string> log)
        {
            _ovrNowSeconds = ovrNowSeconds; _toWorld = toWorld; _log = log;
            for (int i = 0; i < PoolSize; i++) _free[i] = true;
        }

        public void Start(Camera mainCamera, Transform fallbackParent)
        {
            try
            {
                _occ = UnityEngine.Object.FindAnyObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
                if (_occ == null)
                {
                    var host = mainCamera != null ? mainCamera.gameObject : (fallbackParent != null ? fallbackParent.gameObject : null);
                    if (host != null) { _occ = host.AddComponent<AROcclusionManager>(); _added = true; }
                }
                if (UnityEngine.Object.FindAnyObjectByType<ARSession>(FindObjectsInactive.Include) == null)
                {
                    var go = new GameObject("[FinalScan ARSession]"); go.AddComponent<ARSession>(); _sessionAdded = true;
                }
                if (_occ == null) { _log?.Invoke("FS-SENSOR depth: no AROcclusionManager and no camera to host one"); return; }
                _occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
                _occ.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
                _occ.enabled = true;
                if (!_subscribed) { _occ.frameReceived += OnFrame; _subscribed = true; }
                _log?.Invoke("FS-SENSOR depth: started managerAdded=" + _added + " sessionAdded=" + _sessionAdded);
            }
            catch (Exception e) { _log?.Invoke("FS-SENSOR depth start failed: " + e.GetType().Name + ": " + e.Message); }
        }

        public void Stop()
        {
            if (_occ != null)
            {
                if (_subscribed) { try { _occ.frameReceived -= OnFrame; } catch (Exception) { } }
                _subscribed = false;
                try { _occ.enabled = false; } catch (Exception) { }
            }
            LatestDepth?.Release(); LatestDepth = null;
            for (int i = 0; i < PoolSize; i++) { if (_pool[i] != null) { _pool[i].Release(); UnityEngine.Object.Destroy(_pool[i]); _pool[i] = null; } _free[i] = true; }
        }

        /// <summary>Contract §6: after resume the subsystem is restarted (disable now, enable a couple of frames later).</summary>
        public void OnApplicationPause(bool paused, int frameCount)
        {
            if (_occ == null) return;
            if (paused) { try { _occ.enabled = false; } catch (Exception) { } _restartAtFrame = -1; return; }
            _restartAtFrame = frameCount + RestartDelayFrames;
        }

        /// <summary>Per-frame housekeeping: delayed restart and one-shot hand removal.</summary>
        public void Tick(int frameCount)
        {
            if (_occ == null) return;
            if (_restartAtFrame >= 0 && frameCount >= _restartAtFrame)
            {
                _restartAtFrame = -1; Restarts++; _lastTs = 0; _handRemovalTried = false;
                try { _occ.enabled = false; _occ.enabled = true; _log?.Invoke("FS-SENSOR depth: restarted after resume"); } catch (Exception e) { _log?.Invoke("FS-SENSOR depth restart failed: " + e.Message); }
            }
            if (!_handRemovalTried && _occ.enabled)
            {
                XROcclusionSubsystem sub = null;
                try { sub = _occ.subsystem; } catch (Exception) { }
                if (sub != null && sub.running) TryEnableHandRemoval(sub);
            }
        }

        void TryEnableHandRemoval(XROcclusionSubsystem sub)
        {
            _handRemovalTried = true;
            try
            {
                var m = sub.GetType().GetMethod("TrySetHandRemovalEnabled", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                if (m == null) { HandRemoval = "api-missing(" + sub.GetType().Name + ")"; }
                else
                {
                    object r = m.Invoke(sub, new object[] { true });
                    HandRemoval = "requested:" + (r?.ToString() ?? "null");
                }
            }
            catch (Exception e) { HandRemoval = "error:" + e.GetType().Name; }
            _log?.Invoke("FS-SENSOR depth: handRemoval=" + HandRemoval);
        }

        void OnFrame(AROcclusionFrameEventArgs a)
        {
            try { Ingest(a); }
            catch (Exception e) { if (CopyErrors++ < 3) _log?.Invoke("FS-SENSOR depth frame error: " + e.GetType().Name + ": " + e.Message); }
        }

        void Ingest(AROcclusionFrameEventArgs a)
        {
            if (!a.TryGetTimestamp(out long ts)) { NoTimestamp++; return; }
            if (ts == _lastTs) { Repeated++; return; }
            if (ts < _lastTs) { NonMonotonic++; return; }
            _lastTs = ts;
            var ext = a.externalTextures;
            Texture src = ext != null && ext.Count > 0 ? ext[0].texture : null;
            if (src == null) return;
            int slot = -1;
            for (int i = 0; i < PoolSize; i++) if (_free[i]) { slot = i; break; }
            if (slot < 0)
            {
                // the newest frame wins: drop the published one if it is the only holder
                if (LatestDepth != null && LatestDepth.Alive) { var old = LatestDepth; LatestDepth = null; old.Release(); }
                for (int i = 0; i < PoolSize; i++) if (_free[i]) { slot = i; break; }
                if (slot < 0) { PoolExhausted++; return; }
            }
            var dst = EnsureSlot(slot, src);
            if (dst == null) { CopyErrors++; return; }
            if (!Copy(src, dst)) { CopyErrors++; return; }
            _free[slot] = false;

            double ovrNow = _ovrNowSeconds != null ? _ovrNowSeconds() : 0;
            long nowNs = ovrNow > 0 ? (long)(ovrNow * 1e9) : ts;
            var f = new DepthFrame { frameId = _nextId++, xrTimeNs = ts, receiveXrTimeNs = nowNs, ageNs = nowNs - ts, texture = dst, width = dst.width, height = dst.height, poolSlot = slot };
            f.Begin(Free);
            if (a.TryGetPoses(out var poses) && poses != null && poses.Count >= 1)
            {
                f.posesValid = true;
                for (int e = 0; e < 2; e++)
                {
                    Pose p = poses[Math.Min(e, poses.Count - 1)];
                    if (_toWorld != null) p = _toWorld(p);
                    f.poses[e] = p; f.poseMatrices[e] = SensorMath.Trs(p);
                }
            }
            if (a.TryGetFovs(out var fovs) && fovs != null && fovs.Count >= 1)
            {
                f.fovsValid = true;
                for (int e = 0; e < 2; e++)
                {
                    var v = fovs[Math.Min(e, fovs.Count - 1)];
                    f.fovTangents[e] = new Vector4(Mathf.Tan(v.angleLeft), Mathf.Tan(v.angleRight), Mathf.Tan(v.angleUp), Mathf.Tan(v.angleDown));
                }
            }
            if (a.TryGetNearFarPlanes(out var planes)) { f.planesValid = true; f.near = planes.nearZ; f.far = planes.farZ; }
            Frames++; LastAgeNs = f.ageNs; AgeMeanNs += (f.ageNs - AgeMeanNs) / Math.Min(Frames, 64);
            _cadence.Add(ts, nowNs);
            var prev = LatestDepth; LatestDepth = f; prev?.Release();
            DepthReceived?.Invoke(f);
        }

        RenderTexture EnsureSlot(int slot, Texture src)
        {
            int slices = src is Texture2DArray arr ? arr.depth : src is RenderTexture srt ? Math.Max(1, srt.volumeDepth) : 1;
            if (!_copyModeDecided)
            {
                _copyModeDecided = true;
                bool color = src.graphicsFormat != GraphicsFormat.None && !GraphicsFormatUtility.IsDepthFormat(src.graphicsFormat) && !GraphicsFormatUtility.IsStencilFormat(src.graphicsFormat);
                _copyByCopyTexture = color && SystemInfo.copyTextureSupport != UnityEngine.Rendering.CopyTextureSupport.None;
                _log?.Invoke("FS-SENSOR depth: source " + src.width + "x" + src.height + "x" + slices + " fmt=" + src.graphicsFormat + " dim=" + src.dimension + " copy=" + CopyMode);
            }
            var rt = _pool[slot];
            if (rt != null && (rt.width != src.width || rt.height != src.height || rt.volumeDepth != slices)) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            if (rt == null)
            {
                var fmt = _copyByCopyTexture ? src.graphicsFormat : GraphicsFormat.R32_SFloat;
                rt = new RenderTexture(src.width, src.height, 0, fmt) { dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray, volumeDepth = slices, enableRandomWrite = false, useMipMap = false, name = "FS depth slot " + slot };
                if (!rt.Create()) { UnityEngine.Object.Destroy(rt); return null; }
                _pool[slot] = rt;
            }
            return rt;
        }

        bool Copy(Texture src, RenderTexture dst)
        {
            if (_copyByCopyTexture) { Graphics.CopyTexture(src, dst); return true; }
            for (int s = 0; s < dst.volumeDepth; s++) Graphics.Blit(src, dst, s, s);
            return true;
        }

        void Free(DepthFrame f) { if (f.poolSlot >= 0) _free[f.poolSlot] = true; f.poolSlot = -1; f.texture = null; }
    }
}
