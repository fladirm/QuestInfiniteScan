using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using FinalScan.Platform.Native;
using FinalScan.Telemetry;
using Meta.XR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using Debug = UnityEngine.Debug;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// C03 sensor authority (contract §3.2, §4, §6). Owns the two PassthroughCameraAccess streams, the clock gate, the pose
    /// ring, per-eye leases, pairing, the profile governor, Environment Depth and recording/replay. Publishes
    /// <see cref="Latest"/> (newest coherent <see cref="StereoObservation"/>) and <see cref="LatestDepth"/>, requests scan
    /// ticks through <see cref="ISensorSink"/> (≤ 20 Hz) and logs "FS-SENSOR {json}" every <see cref="TelemetryIntervalSeconds"/>.
    ///
    /// Runs after PassthroughCameraAccess.Update (execution order 200 vs 0): MRUK polls its native image in Update and
    /// flips IsUpdatedThisFrame, so the same-frame copy here is the earliest managed point after the producer.
    /// Verified MRUK 205 API: CameraPosition, RequestedResolution/MaxFramerate (settable only while disabled), IsPlaying,
    /// IsUpdatedThisFrame, Timestamp (DateTime, UnixEpoch + micros), Intrinsics {FocalLength, PrincipalPoint, SensorResolution,
    /// LensOffset}, GetTexture(), private _timestampNsMonotonic (XrTime base, read by reflection), GetSupportedResolutions().
    /// Verified OVRPlugin API: GetTimeInSeconds(), GetNodePoseStateAtTime(double seconds, Node) (OVRP ≥ 1.76), GetNodePoseStateImmediate(Node),
    /// Node.Head/EyeLeft/EyeRight, Posef/PoseStatef; conversion to Unity = (x, y, -z) / (-qx, -qy, qz, qw) as OVRCommon.ToOVRPose.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class SensorAuthority : MonoBehaviour
    {
        public const string TelemetryPrefix = "FS-SENSOR";
        public const float TelemetryIntervalSeconds = 2f;
        public const float PermissionTimeoutSeconds = 30f;
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        public const string AndroidCameraPermission = "android.permission.CAMERA";

        [Tooltip("Scan mode requested by the application (Idle/Active/Detail). Drives the capture profile governor.")]
        public ScanMode scanMode = ScanMode.Idle;
        [Tooltip("Thermal warning from the host (OVR thermal / frame misses). Forces Thermal30.")]
        public bool thermalWarning;
        [Tooltip("Initial capture profile applied before the governor makes a transition.")]
        public CaptureProfile initialProfile = CaptureProfile.Normal30;
        [Tooltip("Start Environment Depth ingest.")]
        public bool enableDepth = true;
        [Tooltip("Record the sensor stream to persistentDataPath/recordings/<utc>/ (§4.7).")]
        public bool record;
        [Tooltip("Frame image readback budget per eye while recording.")]
        public float recordFramesPerSecond = 5f;

        public SensorPipeline Pipeline { get; private set; }
        public ISensorSink Sink { get; set; }
        public StereoObservation Latest => Pipeline?.Pairer.Latest;
        public DepthFrame LatestDepth => Replayer != null ? Replayer.LatestDepth : _depth?.LatestDepth;
        public EnvDepthSource Depth => _depth;
        public SensorRecorder Recorder { get; private set; }
        /// <summary>Assign before enabling to run from a recording instead of the cameras.</summary>
        public SensorReplayer Replayer { get; set; }
        public bool PermissionGranted { get; private set; }
        public bool CamerasRunning => _pca[0] != null && _pca[0].IsPlaying && _pca[1] != null && _pca[1].IsPlaying;
        public string MonoClockSource { get; private set; } = "none";
        public string TrackingSpaceSource { get; private set; } = "identity";

        readonly PassthroughCameraAccess[] _pca = new PassthroughCameraAccess[2];
        readonly long[] _lastTicks = new long[2];
        readonly CameraIntrinsicsData[] _intr = new CameraIntrinsicsData[2];
        readonly bool[] _intrLogged = new bool[2];
        EnvDepthSource _depth;
        [Tooltip("Shaders/FinalScanDepthCopy.compute; assigned by the editor setup (the only Environment Depth copy path).")]
        [SerializeField] ComputeShader depthCopyCompute;
        Transform _trackingSpace;
        Camera _mainCamera;
        Coroutine _boot, _apply;
        bool _applying;
        double _lastTelemetry;
        long _pairsAtTelemetry, _framesLAtTelemetry, _framesRAtTelemetry;
        long _poseSamplesRuntime, _poseSamplesImmediate, _poseSamplesFailed;
        static FieldInfo s_monoField; static bool s_monoFieldLooked;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        HostLog _log;

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            Sink ??= new NativeSensorSink();
            Pipeline = new SensorPipeline(Sink, initialProfile);
            Pipeline.Log += Debug.Log;
            ResolveTrackingSpace();
            _depth = new EnvDepthSource(OvrNowSeconds, ToWorld, Debug.Log, depthCopyCompute);
        }

        void OnEnable()
        {
            FinalScan.Host.FinalScanHost.ScanEnabledChanged += OnScanEnabledChanged;
            OnScanEnabledChanged(FinalScan.Host.FinalScanHost.ScanEnabled);
            _log = HostLog.Open(System.IO.Path.Combine(Application.persistentDataPath, "sensor"));
            if (Replayer != null) { Replayer.Attach(Pipeline, Debug.Log); Debug.Log(TelemetryPrefix + " replay: " + Replayer.Directory); return; }
            _boot = StartCoroutine(Boot());
        }

        /// <summary>START/STOP SCAN (C22): the capture profile governor follows the host gate (Idle keeps NORMAL_30, Active allows NORMAL_60).</summary>
        void OnScanEnabledChanged(bool enabled) { if (scanMode != ScanMode.Detail) scanMode = enabled ? ScanMode.Active : ScanMode.Idle; }

        void OnDisable()
        {
            FinalScan.Host.FinalScanHost.ScanEnabledChanged -= OnScanEnabledChanged;
            if (_boot != null) StopCoroutine(_boot); if (_apply != null) StopCoroutine(_apply);
            _boot = _apply = null; _applying = false;
            StopCameras();
            _depth?.Stop();
            Recorder?.Dispose(); Recorder = null;
            Pipeline.Reset();
            DestroyPool(Pipeline.PoolL); DestroyPool(Pipeline.PoolR);
            _log?.Dispose(); _log = null;
        }

        void OnApplicationPause(bool paused)
        {
            _depth?.OnApplicationPause(paused, Time.frameCount);
            if (!paused) { Pipeline.RingL.Clear(); Pipeline.RingR.Clear(); Pipeline.Poses.Clear(); }
        }

        IEnumerator Boot()
        {
            RequestPermissions();
            double end = Time.realtimeSinceStartupAsDouble + PermissionTimeoutSeconds;
            while (!HasPermission(HeadsetCameraPermission) && Time.realtimeSinceStartupAsDouble < end) yield return null;
            PermissionGranted = HasPermission(HeadsetCameraPermission);
            Debug.Log(TelemetryPrefix + " permission headsetCamera=" + PermissionGranted + " camera=" + HasPermission(AndroidCameraPermission) + " pcaSupported=" + SafeIsSupported());
            if (record) Recorder = SensorRecorder.Create(Pipeline, _depth, recordFramesPerSecond, Debug.Log);
            if (enableDepth) _depth.Start(_mainCamera, transform);
            if (!PermissionGranted || !SafeIsSupported()) yield break;
            for (int e = 0; e < 2; e++) _pca[e] = FindOrCreatePca(e == 0 ? PassthroughCameraAccess.CameraPositionType.Left : PassthroughCameraAccess.CameraPositionType.Right);
            yield return ApplyProfile(Pipeline.Profile, "initial");
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            double ovrBefore = OvrNowSeconds();
            long utc = DateTime.UtcNow.Ticks;
            double ovrAfter = OvrNowSeconds();
            long mono = MonoNowNs();
            double ovrNow = ovrAfter > 0 ? ovrAfter : ovrBefore;
            long nowXr = ovrNow > 0 ? (long)(ovrNow * 1e9) : mono;

            if (Replayer != null)
            {
                Replayer.Step(Time.frameCount);
                Telemetry(nowXr);
                return;
            }

            Pipeline.AddClockReference(ovrBefore, utc, ovrAfter, mono);
            SamplePoseRing(ovrNow);
            for (int e = 0; e < 2; e++) PollCamera(e, ovrNow, mono);
            Pipeline.PumpTicks(nowXr);
            if (!_applying && Pipeline.UpdateGovernor(Time.realtimeSinceStartupAsDouble, nowXr, scanMode, thermalWarning) && CamerasConfigured)
                _apply = StartCoroutine(ApplyProfile(Pipeline.Profile, Pipeline.Governor.LastReason));
            _depth?.Tick(Time.frameCount);
            Recorder?.Tick(nowXr);
            Telemetry(nowXr);
        }

        bool CamerasConfigured => _pca[0] != null && _pca[1] != null;

        void SamplePoseRing(double ovrNow)
        {
            if (ovrNow <= 0) return;
            bool ok = TryRuntimeHead(ovrNow, out Pose head, out Vector3 angVel, out Vector3 linVel, out bool immediate);
            if (!ok) { _poseSamplesFailed++; return; }
            if (immediate) _poseSamplesImmediate++; else _poseSamplesRuntime++;
            Pose eyeL = NodePoseAt(ovrNow, OVRPlugin.Node.EyeLeft, out bool okL), eyeR = NodePoseAt(ovrNow, OVRPlugin.Node.EyeRight, out bool okR);
            if (!okL) eyeL = head; if (!okR) eyeR = head;
            Pipeline.PushPose(new PoseSample { xrTimeNs = (long)(ovrNow * 1e9), head = head, eyeLeft = eyeL, eyeRight = eyeR, angularVelocity = angVel, linearVelocity = linVel, valid = true });
        }

        /// <summary>Head pose at an OVR time: GetNodePoseStateAtTime (xrLocateSpace at that time), falling back to the immediate pose only for "now".</summary>
        bool TryRuntimeHead(double seconds, out Pose head, out Vector3 angVel, out Vector3 linVel, out bool immediate)
        {
            head = Pose.identity; angVel = Vector3.zero; linVel = Vector3.zero; immediate = false;
            try
            {
                if (!OVRPlugin.initialized) return false;
                var st = OVRPlugin.GetNodePoseStateAtTime(seconds, OVRPlugin.Node.Head);
                if (IsIdentity(st.Pose))
                {
                    st = OVRPlugin.GetNodePoseStateImmediate(OVRPlugin.Node.Head);
                    if (IsIdentity(st.Pose)) return false;
                    immediate = true;
                }
                head = ToWorld(FromOvr(st.Pose));
                angVel = new Vector3(-st.AngularVelocity.x, -st.AngularVelocity.y, st.AngularVelocity.z);
                linVel = new Vector3(st.Velocity.x, st.Velocity.y, -st.Velocity.z);
                return true;
            }
            catch (Exception) { return false; }
        }

        bool LocateHeadAtCapture(long xrTimeNs, out Pose head)
        {
            head = Pose.identity;
            try
            {
                if (!OVRPlugin.initialized) return false;
                var st = OVRPlugin.GetNodePoseStateAtTime(xrTimeNs * 1e-9, OVRPlugin.Node.Head);
                if (IsIdentity(st.Pose)) return false;
                head = ToWorld(FromOvr(st.Pose));
                return true;
            }
            catch (Exception) { return false; }
        }

        Pose NodePoseAt(double seconds, OVRPlugin.Node node, out bool ok)
        {
            ok = false;
            try
            {
                var st = OVRPlugin.GetNodePoseStateAtTime(seconds, node);
                if (IsIdentity(st.Pose)) return Pose.identity;
                ok = true; return ToWorld(FromOvr(st.Pose));
            }
            catch (Exception) { return Pose.identity; }
        }

        static bool IsIdentity(in OVRPlugin.Posef p)
            => p.Position.x == 0f && p.Position.y == 0f && p.Position.z == 0f && p.Orientation.x == 0f && p.Orientation.y == 0f && p.Orientation.z == 0f && p.Orientation.w == 1f;

        /// <summary>OVRPlugin (OpenXR right-handed) to Unity: same mapping as OVRCommon.ToOVRPose / MRUK.FlipZ.</summary>
        static Pose FromOvr(in OVRPlugin.Posef p)
            => new Pose(new Vector3(p.Position.x, p.Position.y, -p.Position.z), new Quaternion(-p.Orientation.x, -p.Orientation.y, p.Orientation.z, p.Orientation.w));

        Pose ToWorld(Pose tracking)
        {
            if (_trackingSpace == null) return tracking;
            return new Pose(_trackingSpace.TransformPoint(tracking.position), _trackingSpace.rotation * tracking.rotation);
        }

        void ResolveTrackingSpace()
        {
            _mainCamera = Camera.main;
            try
            {
                var rig = FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
                if (rig != null && rig.trackingSpace != null) { _trackingSpace = rig.trackingSpace; TrackingSpaceSource = "OVRCameraRig.trackingSpace"; return; }
            }
            catch (Exception) { }
            try
            {
                var origin = FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
                if (origin != null && origin.TrackablesParent != null) { _trackingSpace = origin.TrackablesParent; TrackingSpaceSource = "XROrigin.TrackablesParent"; if (_mainCamera == null) _mainCamera = origin.Camera; return; }
            }
            catch (Exception) { }
            _trackingSpace = null; TrackingSpaceSource = "identity";
        }

        // ------------------------------------------------------------------ PCA
        void PollCamera(int e, double ovrNow, long monoNow)
        {
            var pca = _pca[e];
            if (pca == null || _applying) return;
            bool playing; try { playing = pca.IsPlaying && pca.IsUpdatedThisFrame; } catch (Exception) { return; }
            if (!playing) return;
            long ticks = pca.Timestamp.Ticks;
            if (ticks == _lastTicks[e]) return;
            _lastTicks[e] = ticks;
            var eye = (CameraEye)e;
            if (!_intr[e].valid) ReadIntrinsics(e, pca);
            long monoField = ReadPcaMonoNs(pca);
            Texture src = null; try { src = pca.GetTexture(); } catch (Exception) { }
            if (!Pipeline.TryBeginFrame(eye, monoField, ticks, ovrNow, monoNow, _intr[e], LocateHeadAtCapture, out var lease)) return;
            if (src != null && !CopyIntoSlot(eye, lease, src)) { Pipeline.AbortFrame(lease); return; }
            Pipeline.CommitFrame(lease);
        }

        bool CopyIntoSlot(CameraEye eye, CameraFrameLease lease, Texture src)
        {
            var pool = Pipeline.Pool(eye);
            var dst = pool.GetTexture(lease.poolSlot) as RenderTexture;
            if (dst == null || dst.width != src.width || dst.height != src.height)
            {
                if (dst != null) { dst.Release(); Destroy(dst); }
                var fmt = src.graphicsFormat != GraphicsFormat.None ? src.graphicsFormat : GraphicsFormat.R8G8B8A8_SRGB;
                dst = new RenderTexture(src.width, src.height, 0, fmt) { useMipMap = false, autoGenerateMips = false, name = "FS pca " + eye + " slot " + lease.poolSlot };
                if (!dst.Create()) { Destroy(dst); return false; }
                pool.SetTexture(lease.poolSlot, dst);
            }
            lease.texture = dst;
            try
            {
                if (src.graphicsFormat == dst.graphicsFormat && SystemInfo.copyTextureSupport != UnityEngine.Rendering.CopyTextureSupport.None) Graphics.CopyTexture(src, dst);
                else Graphics.Blit(src, dst);
                return true;
            }
            catch (Exception ex) { Debug.LogWarning(TelemetryPrefix + " copy failed: " + ex.Message); return false; }
        }

        void ReadIntrinsics(int e, PassthroughCameraAccess pca)
        {
            try
            {
                var i = pca.Intrinsics; var res = pca.CurrentResolution;
                _intr[e] = CameraIntrinsicsData.Create(i.FocalLength.x, i.FocalLength.y, i.PrincipalPoint.x, i.PrincipalPoint.y, res.x, res.y, i.SensorResolution.x, i.SensorResolution.y, i.LensOffset);
                if (_intr[e].valid && !_intrLogged[e])
                {
                    _intrLogged[e] = true;
                    Debug.Log(TelemetryPrefix + " intrinsics " + new JsonWriter().BeginObject().Prop("eye", e == 0 ? "L" : "R").Prop("fx", i.FocalLength.x).Prop("fy", i.FocalLength.y).Prop("cx", i.PrincipalPoint.x).Prop("cy", i.PrincipalPoint.y)
                        .Prop("w", res.x).Prop("h", res.y).Prop("sensorW", i.SensorResolution.x).Prop("sensorH", i.SensorResolution.y)
                        .Prop("lensX", i.LensOffset.position.x).Prop("lensY", i.LensOffset.position.y).Prop("lensZ", i.LensOffset.position.z).EndObject());
                    Recorder?.WriteIntrinsics(e, _intr[e]);
                }
            }
            catch (Exception) { }
        }

        static long ReadPcaMonoNs(PassthroughCameraAccess pca)
        {
            if (!s_monoFieldLooked)
            {
                s_monoFieldLooked = true;
                try { s_monoField = typeof(PassthroughCameraAccess).GetField("_timestampNsMonotonic", BindingFlags.NonPublic | BindingFlags.Instance); } catch (Exception) { s_monoField = null; }
                Debug.Log(TelemetryPrefix + " clock: _timestampNsMonotonic " + (s_monoField != null ? "present" : "MISSING -> path C"));
            }
            if (s_monoField == null) return -1;
            try { return (long)s_monoField.GetValue(pca); } catch (Exception) { return -1; }
        }

        PassthroughCameraAccess FindOrCreatePca(PassthroughCameraAccess.CameraPositionType pos)
        {
            var all = FindObjectsByType<PassthroughCameraAccess>(FindObjectsInactive.Include);
            foreach (var p in all) if (p.CameraPosition == pos) return p;
            var host = new GameObject("[FinalScan] PCA " + pos);
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var pca = host.AddComponent<PassthroughCameraAccess>();
            pca.enabled = false;
            pca.CameraPosition = pos;
            host.SetActive(true);
            return pca;
        }

        /// <summary>Coarse transition (§3.2): disable both streams, set resolution/framerate (only legal while disabled), enable, wait for IsPlaying.</summary>
        IEnumerator ApplyProfile(CaptureProfile profile, string reason)
        {
            _applying = true;
            var spec = CaptureProfiles.Spec(profile);
            Debug.Log(TelemetryPrefix + " profile " + new JsonWriter().BeginObject().Prop("profile", profile.ToString()).Prop("w", spec.Resolution.x).Prop("h", spec.Resolution.y).Prop("fps", spec.MaxFramerate).Prop("reason", reason).EndObject());
            Recorder?.WriteProfile(profile, reason);
            for (int e = 0; e < 2; e++) { var p = _pca[e]; if (p != null && p.enabled) p.enabled = false; }
            yield return null;
            for (int e = 0; e < 2; e++)
            {
                var p = _pca[e]; if (p == null) continue;
                try { p.RequestedResolution = spec.Resolution; p.MaxFramerate = spec.MaxFramerate; p.enabled = true; }
                catch (Exception ex) { Debug.LogWarning(TelemetryPrefix + " profile apply failed eye " + e + ": " + ex.Message); }
                _lastTicks[e] = 0; _intr[e] = default;
            }
            Pipeline.OnProfileApplied();
            double deadline = Time.realtimeSinceStartupAsDouble + 8.0;
            while (Time.realtimeSinceStartupAsDouble < deadline && !CamerasRunning) yield return null;
            Debug.Log(TelemetryPrefix + " profile applied running=" + CamerasRunning);
            _applying = false; _apply = null;
        }

        void StopCameras()
        {
            for (int e = 0; e < 2; e++) { try { if (_pca[e] != null && _pca[e].enabled) _pca[e].enabled = false; } catch (Exception) { } _lastTicks[e] = 0; }
        }

        static void DestroyPool(FrameTexturePool pool)
        {
            foreach (var t in pool.DrainTextures()) { if (t is RenderTexture rt) { rt.Release(); Destroy(rt); } else if (t != null) Destroy(t); }
        }

        // ------------------------------------------------------------------ clocks
        static double OvrNowSeconds() { try { return OVRPlugin.initialized ? OVRPlugin.GetTimeInSeconds() : -1; } catch (Exception) { return -1; } }

        /// <summary>CLOCK_MONOTONIC ns: native lib when present, else Stopwatch (CLOCK_MONOTONIC on Android Mono/IL2CPP). Path A verification decides whether the base matches XrTime.</summary>
        long MonoNowNs()
        {
            try
            {
                if (FinalScanNative.Available) { MonoClockSource = "FsNative_MonotonicNowNs"; return FinalScanNative.MonotonicNowNs(); }
            }
            catch (Exception) { }
            MonoClockSource = "Stopwatch";
            return (long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency));
        }

        // ------------------------------------------------------------------ permissions
        void RequestPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var need = new List<string>();
                if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission)) need.Add(HeadsetCameraPermission);
                if (!Permission.HasUserAuthorizedPermission(AndroidCameraPermission)) need.Add(AndroidCameraPermission);
                if (need.Count == 0) return;
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += p => Debug.Log(TelemetryPrefix + " permission granted " + p);
                cb.PermissionDenied += p => Debug.LogWarning(TelemetryPrefix + " permission denied " + p);
                Permission.RequestUserPermissions(need.ToArray(), cb);
            }
            catch (Exception e) { Debug.LogWarning(TelemetryPrefix + " permission request failed: " + e.Message); }
#endif
        }

        static bool HasPermission(string p)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { return Permission.HasUserAuthorizedPermission(p); } catch (Exception) { return false; }
#else
            return true;
#endif
        }

        static bool SafeIsSupported() { try { return PassthroughCameraAccess.IsSupported; } catch (Exception) { return false; } }

        // ------------------------------------------------------------------ telemetry
        void Telemetry(long nowXr)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastTelemetry < TelemetryIntervalSeconds) return;
            double interval = _lastTelemetry > 0 ? now - _lastTelemetry : TelemetryIntervalSeconds;
            _lastTelemetry = now;
            long pairs = Pipeline.Pairer.Pairs, fl = Pipeline.CadenceL.Frames, fr = Pipeline.CadenceR.Frames;
            var w = new JsonWriter(1024).BeginObject().Prop("t", now).Prop("xrTimeNs", nowXr).Prop("replay", Replayer != null).Prop("camerasRunning", CamerasRunning).Prop("applying", _applying).Prop("permission", PermissionGranted);
            Pipeline.WriteTelemetry(w, nowXr, interval, pairs - _pairsAtTelemetry, fl - _framesLAtTelemetry, fr - _framesRAtTelemetry);
            _pairsAtTelemetry = pairs; _framesLAtTelemetry = fl; _framesRAtTelemetry = fr;
            w.Prop("poseSamplesAtTime", _poseSamplesRuntime).Prop("poseSamplesImmediate", _poseSamplesImmediate).Prop("poseSamplesFailed", _poseSamplesFailed)
             .Prop("monoClock", MonoClockSource).Prop("trackingSpace", TrackingSpaceSource);
            if (_depth != null)
            {
                w.BeginObject("depth").Prop("running", _depth.Running).Prop("fps", _depth.Fps(nowXr)).Prop("frames", _depth.Frames).Prop("repeated", _depth.Repeated).Prop("nonMonotonic", _depth.NonMonotonic)
                 .Prop("ageMs", _depth.LastAgeNs / 1e6).Prop("ageMeanMs", _depth.AgeMeanNs / 1e6).Prop("copy", _depth.CopyMode).Prop("copyErrors", _depth.CopyErrors).Prop("poolExhausted", _depth.PoolExhausted)
                 .Prop("restarts", _depth.Restarts).Prop("handRemoval", _depth.HandRemoval);
                var d = _depth.LatestDepth;
                if (d != null) w.Prop("w", d.width).Prop("h", d.height).Prop("near", d.near).Prop("far", d.far).Prop("poses", d.posesValid).Prop("fovs", d.fovsValid);
                w.EndObject();
            }
            if (Recorder != null) Recorder.WriteStats(w.BeginObject("recorder")).EndObject();
            if (Replayer != null) Replayer.WriteStats(w.BeginObject("replayer")).EndObject();
            string json = w.EndObject().ToString();
            if (_log != null) _log.Emit(TelemetryPrefix, json); else Debug.Log(TelemetryPrefix + " " + json);
            Recorder?.WriteTelemetry(json);
        }
    }
}
