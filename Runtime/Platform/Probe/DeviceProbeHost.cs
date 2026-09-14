using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using FinalScan.Platform.Native;
using FinalScan.Platform.Sensor;
using FinalScan.Telemetry;
using Meta.XR;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace FinalScan.Platform.Probe
{
    /// <summary>
    /// C01 device probe (contract §3, §4, §6, §16, §20, §26). Attached by the editor setup to "[FinalScan]".
    /// Runs a scripted schedule as a coroutine state machine; every result is ONE logcat line "FS-PROBE {json}"
    /// and the same line appended to Application.persistentDataPath/probe/probe-&lt;utc&gt;.jsonl.
    ///
    /// Schedule (ordered so that per-phase PSS deltas map to components, §16):
    ///   phase            dur   cameras                          measures
    ///   boot              5 s  none (XR only)                   SystemInfo, refresh rate, native device report, AHB import
    ///   depth            15 s  Env Depth only                   cadence, XrTime deltas, texture, poses/fovs, age      -> +Env Depth
    ///   pca_L_30_960     10 s  PCA L only 1280x960@30           per-eye cadence/latency/pose                         -> +L
    ///   pca_30_960       20 s  PCA L+R 1280x960@30              + pairing delta                                      -> +L+R
    ///   pca_60_960       20 s  PCA L+R 1280x960@60
    ///   pca_60_1280      20 s  PCA L+R 1280x1280@60 (if supported, else skipped with reason)
    ///   pca_30_1280      20 s  PCA L+R 1280x1280@30 (if supported)
    ///   camera2_excl     15 s  Camera2 NDK probe, PCA stopped   native camera report (TIMESTAMP_SOURCE, cadence, latency)
    ///   camera2_pca      15 s  Camera2 NDK probe + PCA L+R 30   same, to see whether cameras are exclusive
    ///   vulkan          ~22 s  none                             empty submit, timestamp, pipeline binary, overlap 10 s, baseline 5 s
    ///   thermal           0 s  none                             OVR/Android thermal + perf levels snapshot
    ///   done              -    none                             summary, idle
    /// Background samplers during the whole run (every 5 s): memory (PSS via ActivityManager + Unity profiler),
    /// thermal snapshot, clock triplet (OVR seconds / CLOCK_MONOTONIC / CLOCK_BOOTTIME / UTC) for the clock-domain gate.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class DeviceProbeHost : MonoBehaviour
    {
        // ---- schedule constants (seconds) ----
        public const float BootSettleSeconds = 5f;
        public const float DepthSeconds = 15f;
        public const float PcaLeftOnlySeconds = 10f;
        public const float PcaProfileSeconds = 20f;
        public const float Camera2Seconds = 15f;
        public const float VulkanPreBaselineSeconds = 5f;
        public const float VulkanOverlapSeconds = 10f;
        public const float VulkanPostBaselineSeconds = 5f;
        public const float SamplerIntervalSeconds = 5f;
        public const float PcaStartTimeoutSeconds = 8f;
        public const float PermissionTimeoutSeconds = 30f;
        public const int TraceFramesPerEye = 300;
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        public const string AndroidCameraPermission = "android.permission.CAMERA";

        [Tooltip("Run the probe automatically on Start.")]
        public bool autoRun = true;
        [Tooltip("Target XR display refresh rate requested in the boot phase.")]
        public float targetRefreshRate = 72f;

        ProbeLog _log;
        string _phase = "init";
        double _phaseStart;
        double _runStart;
        TextMesh _status;
        readonly List<string> _summary = new List<string>();
        readonly List<ClockTriplet> _triplets = new List<ClockTriplet>();
        Coroutine _samplerLoop;

        // PCA
        readonly PcaEyeProbe[] _eyes = new PcaEyeProbe[2];
        readonly long[] _ringL = new long[16]; readonly long[] _ringR = new long[16]; int _ringLn, _ringRn;
        readonly SampleStats _pairAbsMs = new SampleStats(), _pairSignedMs = new SampleStats();
        int _pairSamples;
        static FieldInfo s_pcaMonoField;
        static bool s_pcaMonoFieldLooked;
        bool _pcaSampling;
        Vector2Int[] _supportedL, _supportedR;

        // Depth
        AROcclusionManager _occ;
        readonly DepthProbe _depth = new DepthProbe();
        bool _depthSubscribed;

        // Vulkan
        CommandBuffer _cb;
        IntPtr _renderEventFunc;
        FrameSegment _frames;
        readonly FrameTiming[] _ft = new FrameTiming[1];
        readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();
        string[] _xrStatNames = { "gpuTimeLastFrame", "cpuTimeLastFrame", "droppedFrameCount", "framePresentCount", "motionToPhoton", "framesInFlight" };

        // Android
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _context, _activityManager, _powerManager;
        AndroidJavaClass _debugClass;
        int _pid;
        bool _androidInit;
#endif

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            string dir = Path.Combine(Application.persistentDataPath, "probe");
            try { _log = ProbeLog.OpenFile(dir, DateTime.UtcNow, s => Debug.Log(s), () => Time.realtimeSinceStartupAsDouble); }
            catch (Exception e)
            {
                Debug.LogWarning("[FinalScan] probe jsonl unavailable: " + e.Message);
                _log = new ProbeLog(null, s => Debug.Log(s), () => Time.realtimeSinceStartupAsDouble, () => DateTime.UtcNow);
            }
            _log.Emit("log_open", w => w.Prop("path", _log.FilePath).Prop("persistentDataPath", Application.persistentDataPath));
        }

        void Start()
        {
            CreateStatusText();
            if (autoRun) StartCoroutine(RunSchedule());
        }

        void OnDestroy()
        {
            StopPcaSampling();
            UnsubscribeDepth();
            _cb?.Release();
            _log?.Dispose();
        }

        void Update()
        {
            if (_pcaSampling) SamplePca();
            if (_frames != null) _frames.Tick(this);
            if (_status != null && Time.frameCount % 15 == 0) UpdateStatusText();
        }

        // ------------------------------------------------------------------ schedule
        public IEnumerator RunSchedule()
        {
            _runStart = Time.realtimeSinceStartupAsDouble;
            _samplerLoop = StartCoroutine(SamplerLoop());

            yield return Phase("boot", BootPhase());
            yield return Phase("depth", DepthPhase());
            yield return Phase("pca_L_30_960", PcaProfile("pca_L_30_960", 1280, 960, 30, true, false, PcaLeftOnlySeconds));
            yield return Phase("pca_30_960", PcaProfile("pca_30_960", 1280, 960, 30, true, true, PcaProfileSeconds));
            yield return Phase("pca_60_960", PcaProfile("pca_60_960", 1280, 960, 60, true, true, PcaProfileSeconds));
            yield return Phase("pca_60_1280", PcaProfile("pca_60_1280", 1280, 1280, 60, true, true, PcaProfileSeconds));
            yield return Phase("pca_30_1280", PcaProfile("pca_30_1280", 1280, 1280, 30, true, true, PcaProfileSeconds));
            StopAllPca();
            yield return Phase("camera2_excl", Camera2Phase("exclusive", false));
            yield return Phase("camera2_pca", Camera2Phase("with_pca", true));
            StopAllPca();
            yield return Phase("vulkan", VulkanPhase());
            yield return Phase("thermal", ThermalPhase());
            yield return Phase("done", DonePhase());
        }

        IEnumerator Phase(string name, IEnumerator body)
        {
            _phase = name; _log.Phase = name; _phaseStart = Time.realtimeSinceStartupAsDouble;
            _log.Emit("phase", w => w.Prop("name", name).Prop("action", "begin").Prop("elapsedRun", _phaseStart - _runStart));
            UpdateStatusText();
            // Run body manually so one exception does not kill the whole schedule.
            while (true)
            {
                object cur; bool more;
                try { more = body.MoveNext(); cur = more ? body.Current : null; }
                catch (Exception e) { _log.Error(name, e); break; }
                if (!more) break;
                yield return cur;
            }
            _log.Emit("phase", w => w.Prop("name", name).Prop("action", "end").Prop("seconds", Time.realtimeSinceStartupAsDouble - _phaseStart));
        }

        IEnumerator Wait(float seconds)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < end) yield return null;
        }

        // ------------------------------------------------------------------ boot
        IEnumerator BootPhase()
        {
            _log.Emit("boot", w =>
            {
                w.BeginObject("app").Prop("version", FinalScanBootstrap.Version).Prop("unity", Application.unityVersion)
                 .Prop("targetFrameRate", Application.targetFrameRate).Prop("vSyncCount", QualitySettings.vSyncCount)
                 .Prop("platform", Application.platform.ToString()).Prop("identifier", Application.identifier).EndObject();
                w.BeginObject("sysinfo")
                 .Prop("deviceModel", SystemInfo.deviceModel).Prop("deviceName", SystemInfo.deviceName)
                 .Prop("os", SystemInfo.operatingSystem).Prop("cpu", SystemInfo.processorType).Prop("cpuCount", SystemInfo.processorCount)
                 .Prop("cpuMHz", SystemInfo.processorFrequency).Prop("systemMemoryMB", SystemInfo.systemMemorySize)
                 .Prop("gpu", SystemInfo.graphicsDeviceName).Prop("gpuVendor", SystemInfo.graphicsDeviceVendor)
                 .Prop("gpuVersion", SystemInfo.graphicsDeviceVersion).Prop("gpuType", SystemInfo.graphicsDeviceType.ToString())
                 .Prop("gpuMemoryMB", SystemInfo.graphicsMemorySize).Prop("supportsAsyncCompute", SystemInfo.supportsAsyncCompute)
                 .Prop("supportsComputeShaders", SystemInfo.supportsComputeShaders).Prop("supportsAsyncGPUReadback", SystemInfo.supportsAsyncGPUReadback)
                 .Prop("maxTextureSize", SystemInfo.maxTextureSize).Prop("multiThreadedRendering", SystemInfo.graphicsMultiThreaded)
                 .EndObject();
                WriteAndroidBuild(w);
                w.BeginObject("native").Prop("available", FinalScanNative.Available).Prop("vulkanReady", FinalScanNative.VulkanReady).EndObject();
            });

            RequestPermissions();
            yield return SetRefreshRate();

            // Native device report + AHB import probe (needs Vulkan ready; the .so initialises on plugin load).
            for (int i = 0; i < 90 && FinalScanNative.Available && !FinalScanNative.VulkanReady; i++) yield return null;
            string report = null; int ahb = int.MinValue; string err = null;
            try
            {
                if (FinalScanNative.Available)
                {
                    ahb = FinalScanNative.VulkanReady ? FinalScanNative.AhbImportProbe() : -999;
                    report = FinalScanNative.DeviceReportJson();
                }
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
            _log.Emit("native_device", w =>
            {
                w.Prop("available", FinalScanNative.Available).Prop("vulkanReady", FinalScanNative.VulkanReady)
                 .Prop("ahbImportRc", ahb).Prop("error", err).PropRaw("deviceReport", IsJsonObject(report) ? report : null)
                 .Prop("deviceReportRaw", IsJsonObject(report) ? null : report);
            });
            _summary.Add("native=" + (FinalScanNative.Available ? (FinalScanNative.VulkanReady ? "vk" : "novk") : "none") + " ahb=" + ahb);

            double permEnd = Time.realtimeSinceStartupAsDouble + PermissionTimeoutSeconds;
            while (!HasPermission(HeadsetCameraPermission) && Time.realtimeSinceStartupAsDouble < permEnd) yield return null;
            _log.Emit("permission", w => w.Prop("headsetCamera", HasPermission(HeadsetCameraPermission)).Prop("androidCamera", HasPermission(AndroidCameraPermission)));

            yield return Wait(BootSettleSeconds);
        }

        static bool IsJsonObject(string s) => !string.IsNullOrWhiteSpace(s) && s.TrimStart().StartsWith("{", StringComparison.Ordinal) && s.TrimEnd().EndsWith("}", StringComparison.Ordinal);

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
                cb.PermissionGranted += p => _log.Emit("permission_result", w => w.Prop("permission", p).Prop("granted", true));
                cb.PermissionDenied += p => _log.Emit("permission_result", w => w.Prop("permission", p).Prop("granted", false));
                Permission.RequestUserPermissions(need.ToArray(), cb);
            }
            catch (Exception e) { _log.Error("RequestPermissions", e); }
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

        IEnumerator SetRefreshRate()
        {
            float before = -1, after = -1; float[] avail = null; bool ovrInit = false; string ovrErr = null, oxrErr = null; bool ovrSet = false, oxrSet = false;
            string xrBefore = DescribeDisplays(out float xrRateBefore);
            try
            {
                ovrInit = OVRPlugin.initialized;
                if (ovrInit)
                {
                    avail = OVRPlugin.systemDisplayFrequenciesAvailable;
                    before = OVRPlugin.systemDisplayFrequency;
                    bool has = false; if (avail != null) foreach (var f in avail) if (Mathf.Abs(f - targetRefreshRate) < 0.5f) has = true;
                    if (has || avail == null || avail.Length == 0)
                    {
                        if (OVRManager.instance != null && OVRManager.display != null) OVRManager.display.displayFrequency = targetRefreshRate;
                        else OVRPlugin.systemDisplayFrequency = targetRefreshRate;
                        ovrSet = true;
                    }
                }
            }
            catch (Exception e) { ovrErr = e.GetType().Name + ": " + e.Message; }
            // Unity OpenXR path: UnityEngine.XR.OpenXR.Features.Meta.MetaOpenXRDisplaySubsystemExtensions.TryRequestDisplayRefreshRate
            // (com.unity.xr.meta-openxr, needs the Meta Quest Display Utilities feature) - called by reflection to avoid a hard reference.
            try
            {
                var t = Type.GetType("UnityEngine.XR.OpenXR.Features.Meta.MetaOpenXRDisplaySubsystemExtensions, Unity.XR.MetaOpenXR");
                var m = t?.GetMethod("TryRequestDisplayRefreshRate", BindingFlags.Public | BindingFlags.Static);
                SubsystemManager.GetSubsystems(_displays);
                if (m != null && _displays.Count > 0) oxrSet = (bool)m.Invoke(null, new object[] { _displays[0], targetRefreshRate });
                else oxrErr = t == null ? "type unavailable" : (m == null ? "method unavailable" : "no XRDisplaySubsystem");
            }
            catch (Exception e) { oxrErr = e.GetType().Name + ": " + (e.InnerException?.Message ?? e.Message); }
            yield return Wait(1f);
            try { if (ovrInit) after = OVRPlugin.systemDisplayFrequency; } catch (Exception) { }
            string xrAfter = DescribeDisplays(out float xrRateAfter);
            _log.Emit("display_refresh", w =>
            {
                w.Prop("requested", targetRefreshRate).Prop("ovrInitialized", ovrInit).Prop("ovrBefore", before).Prop("ovrAfter", after)
                 .Prop("ovrSetAttempted", ovrSet).Prop("ovrError", ovrErr).Prop("openxrSet", oxrSet).Prop("openxrError", oxrErr)
                 .Prop("xrDisplayRateBefore", xrRateBefore).Prop("xrDisplayRateAfter", xrRateAfter)
                 .Prop("displaysBefore", xrBefore).Prop("displaysAfter", xrAfter);
                w.BeginArray("available"); if (avail != null) foreach (var f in avail) w.Value(f); w.EndArray();
            });
            _summary.Add("hz=" + (after > 0 ? after.ToString("F0") : xrRateAfter.ToString("F0")));
        }

        string DescribeDisplays(out float rate)
        {
            rate = -1;
            try
            {
                SubsystemManager.GetSubsystems(_displays);
                var sb = new System.Text.StringBuilder();
                foreach (var d in _displays)
                {
                    float r; bool ok = d.TryGetDisplayRefreshRate(out r);
                    if (ok && rate < 0) rate = r;
                    sb.Append(d.subsystemDescriptor?.id).Append(" running=").Append(d.running).Append(" rate=").Append(ok ? r.ToString("F2", CultureInfo.InvariantCulture) : "n/a").Append("; ");
                }
                return sb.ToString();
            }
            catch (Exception e) { return "error " + e.Message; }
        }

        // ------------------------------------------------------------------ depth
        IEnumerator DepthPhase()
        {
            bool added = false, sessionAdded = false; string err = null;
            try
            {
                _occ = FindAnyObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
                if (_occ == null)
                {
                    var cam = MainCamera();
                    if (cam != null) { _occ = cam.gameObject.AddComponent<AROcclusionManager>(); added = true; }
                }
                if (FindAnyObjectByType<ARSession>(FindObjectsInactive.Include) == null)
                {
                    var go = new GameObject("[FinalScan ARSession]"); go.AddComponent<ARSession>(); sessionAdded = true;
                }
                if (_occ != null)
                {
                    _occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
                    _occ.enabled = true;
                }
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
            // Give the subsystem a few frames to start.
            for (int i = 0; i < 30; i++) yield return null;
            string descriptor = null; bool hasSub = false; string mode = null;
            try { hasSub = _occ != null && _occ.subsystem != null; descriptor = _occ?.subsystem?.subsystemDescriptor?.id; mode = _occ?.currentEnvironmentDepthMode.ToString(); } catch (Exception e) { err = (err ?? "") + " | " + e.Message; }
            _log.Emit("depth_start", w => w.Prop("found", _occ != null).Prop("addedManager", added).Prop("addedSession", sessionAdded)
                .Prop("subsystem", hasSub).Prop("descriptor", descriptor).Prop("currentMode", mode).Prop("error", err));
            if (_occ == null) yield break;
            _depth.Reset();
            _occ.frameReceived += OnDepthFrame; _depthSubscribed = true;
            yield return Wait(DepthSeconds);
            UnsubscribeDepth();
            double fps = _depth.Frames > 1 ? (_depth.Frames - 1) / (_depth.LastUnity - _depth.FirstUnity) : 0;
            _log.Emit("depth_profile", w =>
            {
                w.Prop("seconds", DepthSeconds).Prop("frames", _depth.Frames).Prop("fps", fps)
                 .Prop("noTimestamp", _depth.NoTimestamp).Prop("nonMonotonic", _depth.NonMonotonic)
                 .Prop("lastTimestampNs", _depth.LastTs)
                 .Prop("posesOk", _depth.PosesOk).Prop("poseCount", _depth.PoseCount).Prop("fovsOk", _depth.FovsOk).Prop("fovCount", _depth.FovCount)
                 .Prop("planesOk", _depth.PlanesOk).Prop("nearZ", _depth.NearZ).Prop("farZ", _depth.FarZ)
                 .Prop("externalTextures", _depth.ExtTexCount).Prop("textures", _depth.TexCount)
                 .Prop("texWidth", _depth.TexW).Prop("texHeight", _depth.TexH).Prop("texSlices", _depth.TexSlices)
                 .Prop("texDimension", _depth.TexDim).Prop("texFormat", _depth.TexFormat).Prop("texType", _depth.TexType);
                w.BeginArray("fov0Deg"); foreach (var f in _depth.Fov0) w.Value(f); w.EndArray();
                _depth.DeltaMs.WriteTo(w, "deltaMs"); _depth.DeltaMs.WriteHistogram(w, "deltaHistMs", 2.0, 40);
                _depth.UnityDeltaMs.WriteTo(w, "unityCallbackDeltaMs");
                _depth.AgeOvrMs.WriteTo(w, "ageVsOvrMs"); _depth.AgeMonoMs.WriteTo(w, "ageVsMonotonicMs"); _depth.AgeBootMs.WriteTo(w, "ageVsBoottimeMs");
                _depth.PoseDeltaMm.WriteTo(w, "poseDeltaMm"); _depth.PoseDeltaDeg.WriteTo(w, "poseDeltaDeg");
            });
            _summary.Add("depth=" + fps.ToString("F1") + "Hz age=" + _depth.AgeOvrMs.Percentile(50).ToString("F0") + "ms");
            try { _occ.enabled = false; } catch (Exception) { }
        }

        void UnsubscribeDepth()
        {
            if (_depthSubscribed && _occ != null) { try { _occ.frameReceived -= OnDepthFrame; } catch (Exception) { } }
            _depthSubscribed = false;
        }

        void OnDepthFrame(AROcclusionFrameEventArgs a)
        {
            try { _depth.OnFrame(a, OvrNowSeconds(), MonoNs(), BootNs()); }
            catch (Exception e) { if (_depth.Errors++ < 3) _log.Error("OnDepthFrame", e); }
        }

        sealed class DepthProbe
        {
            public int Frames, NoTimestamp, NonMonotonic, PosesOk, PoseCount, FovsOk, FovCount, PlanesOk, ExtTexCount, TexCount, Errors;
            public long LastTs; public double FirstUnity, LastUnity, NearZ = double.NaN, FarZ = double.NaN;
            public int TexW = -1, TexH = -1, TexSlices = -1; public string TexDim, TexFormat, TexType;
            public readonly float[] Fov0 = new float[4];
            public readonly SampleStats DeltaMs = new SampleStats(), UnityDeltaMs = new SampleStats(), AgeOvrMs = new SampleStats(), AgeMonoMs = new SampleStats(), AgeBootMs = new SampleStats(), PoseDeltaMm = new SampleStats(), PoseDeltaDeg = new SampleStats();
            bool _havePose; Pose _lastPose; bool _texLogged;

            public void Reset()
            {
                Frames = NoTimestamp = NonMonotonic = PosesOk = PoseCount = FovsOk = FovCount = PlanesOk = ExtTexCount = TexCount = Errors = 0; LastTs = 0; FirstUnity = LastUnity = 0;
                DeltaMs.Reset(); UnityDeltaMs.Reset(); AgeOvrMs.Reset(); AgeMonoMs.Reset(); AgeBootMs.Reset(); PoseDeltaMm.Reset(); PoseDeltaDeg.Reset(); _havePose = false; _texLogged = false;
            }

            public void OnFrame(AROcclusionFrameEventArgs a, double ovrNow, long monoNow, long bootNow)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (Frames == 0) FirstUnity = now; else UnityDeltaMs.Add((now - LastUnity) * 1000.0);
                LastUnity = now; Frames++;
                if (a.TryGetTimestamp(out long ts))
                {
                    if (LastTs > 0) { double d = (ts - LastTs) / 1e6; if (d <= 0) NonMonotonic++; else DeltaMs.Add(d); }
                    LastTs = ts;
                    if (ovrNow > 0) AgeOvrMs.Add((ovrNow * 1e9 - ts) / 1e6);
                    if (monoNow > 0) AgeMonoMs.Add((monoNow - ts) / 1e6);
                    if (bootNow > 0) AgeBootMs.Add((bootNow - ts) / 1e6);
                }
                else NoTimestamp++;
                var ext = a.externalTextures;
                int extCount = ext != null ? ext.Count : 0;
                if (!_texLogged && extCount > 0)
                {
                    Texture t = ext[0].texture;
                    if (t != null)
                    {
                        ExtTexCount = extCount; TexCount = extCount;
                        TexW = t.width; TexH = t.height; TexDim = t.dimension.ToString(); TexType = t.GetType().Name;
                        try { TexFormat = t.graphicsFormat.ToString(); } catch (Exception) { TexFormat = "n/a"; }
                        if (t is Texture2DArray arr) TexSlices = arr.depth;
                        else if (t is RenderTexture rt) TexSlices = rt.volumeDepth;
                        _texLogged = true;
                    }
                }
                if (a.TryGetPoses(out var poses))
                {
                    PosesOk++; PoseCount = poses.Count;
                    if (poses.Count > 0)
                    {
                        Pose p = poses[0];
                        if (_havePose) { PoseDeltaMm.Add(Vector3.Distance(p.position, _lastPose.position) * 1000.0); PoseDeltaDeg.Add(Quaternion.Angle(p.rotation, _lastPose.rotation)); }
                        _lastPose = p; _havePose = true;
                    }
                }
                if (a.TryGetFovs(out var fovs))
                {
                    FovsOk++; FovCount = fovs.Count;
                    if (fovs.Count > 0) { var f = fovs[0]; Fov0[0] = f.angleLeft * Mathf.Rad2Deg; Fov0[1] = f.angleRight * Mathf.Rad2Deg; Fov0[2] = f.angleUp * Mathf.Rad2Deg; Fov0[3] = f.angleDown * Mathf.Rad2Deg; }
                }
                if (a.TryGetNearFarPlanes(out var planes)) { PlanesOk++; NearZ = planes.nearZ; FarZ = planes.farZ; }
            }
        }

        // ------------------------------------------------------------------ PCA
        sealed class PcaEyeProbe
        {
            public PassthroughCameraAccess Pca; public string Eye; public bool Wanted;
            public int Frames, NonMonotonic; public long LastTsTicks, LastMonoNs; public double FirstUnity, LastUnity; public int LastUnityFrame;
            public readonly SampleStats DeltaMs = new SampleStats(), LatencyUtcMs = new SampleStats(), LatencyMonoMs = new SampleStats(), LatencyOvrMs = new SampleStats(), PosDeltaMm = new SampleStats(), RotDeltaDeg = new SampleStats(), UnityFramesBetween = new SampleStats();
            public readonly List<long> TraceTsUs = new List<long>(), TraceMonoNs = new List<long>(); public readonly List<double> TraceUnityMs = new List<double>();
            public bool HavePose; public Pose LastPose; public int MonoFieldMissing;
            public void Reset()
            {
                Frames = NonMonotonic = 0; LastTsTicks = 0; LastMonoNs = 0; FirstUnity = LastUnity = 0; LastUnityFrame = 0; HavePose = false; MonoFieldMissing = 0;
                DeltaMs.Reset(); LatencyUtcMs.Reset(); LatencyMonoMs.Reset(); LatencyOvrMs.Reset(); PosDeltaMm.Reset(); RotDeltaDeg.Reset(); UnityFramesBetween.Reset();
                TraceTsUs.Clear(); TraceMonoNs.Clear(); TraceUnityMs.Clear();
            }
        }

        static long ReadPcaMonoNs(PassthroughCameraAccess pca)
        {
            if (!s_pcaMonoFieldLooked)
            {
                s_pcaMonoFieldLooked = true;
                try { s_pcaMonoField = typeof(PassthroughCameraAccess).GetField("_timestampNsMonotonic", BindingFlags.NonPublic | BindingFlags.Instance); } catch (Exception) { s_pcaMonoField = null; }
            }
            if (s_pcaMonoField == null) return -1;
            try { return (long)s_pcaMonoField.GetValue(pca); } catch (Exception) { return -1; }
        }

        PassthroughCameraAccess FindOrCreatePca(PassthroughCameraAccess.CameraPositionType pos, out bool created)
        {
            created = false;
            var all = FindObjectsByType<PassthroughCameraAccess>(FindObjectsInactive.Include);
            foreach (var p in all) if (p.CameraPosition == pos) return p;
            var host = new GameObject("[FinalScan] PCA " + pos);
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var pca = host.AddComponent<PassthroughCameraAccess>();
            pca.enabled = false;
            pca.CameraPosition = pos;
            host.SetActive(true);
            created = true;
            return pca;
        }

        IEnumerator PcaProfile(string profile, int w, int h, int fps, bool left, bool right, float seconds)
        {
            if (!HasPermission(HeadsetCameraPermission))
            {
                _log.Emit("pca_skip", x => x.Prop("profile", profile).Prop("reason", "no HEADSET_CAMERA permission"));
                yield break;
            }
            if (_supportedL == null)
            {
                try { _supportedL = PassthroughCameraAccess.GetSupportedResolutions(PassthroughCameraAccess.CameraPositionType.Left); _supportedR = PassthroughCameraAccess.GetSupportedResolutions(PassthroughCameraAccess.CameraPositionType.Right); }
                catch (Exception e) { _log.Error("GetSupportedResolutions", e); _supportedL = _supportedR = Array.Empty<Vector2Int>(); }
                _log.Emit("pca_supported", x =>
                {
                    x.Prop("isSupported", SafeIsSupported());
                    x.BeginArray("left"); foreach (var r in _supportedL) x.BeginArray().Value(r.x).Value(r.y).EndArray(); x.EndArray();
                    x.BeginArray("right"); foreach (var r in _supportedR) x.BeginArray().Value(r.x).Value(r.y).EndArray(); x.EndArray();
                });
            }
            bool supported = _supportedL.Length == 0; // unknown list -> try anyway
            foreach (var r in _supportedL) if (r.x == w && r.y == h) supported = true;
            if (!supported)
            {
                _log.Emit("pca_skip", x => x.Prop("profile", profile).Prop("reason", w + "x" + h + " not in GetSupportedResolutions"));
                _summary.Add(profile + "=unsupported");
                yield break;
            }

            // (Re)configure both eyes. MaxFramerate/RequestedResolution only apply while the component is disabled.
            StopPcaSampling();
            for (int e = 0; e < 2; e++)
            {
                bool want = e == 0 ? left : right;
                var pos = e == 0 ? PassthroughCameraAccess.CameraPositionType.Left : PassthroughCameraAccess.CameraPositionType.Right;
                var probe = _eyes[e] ?? (_eyes[e] = new PcaEyeProbe { Eye = e == 0 ? "L" : "R" });
                probe.Wanted = want;
                bool created = false; string err = null;
                try
                {
                    if (probe.Pca == null) probe.Pca = FindOrCreatePca(pos, out created);
                    var pca = probe.Pca;
                    if (pca.enabled) pca.enabled = false;
                    if (!want) continue;
                    pca.RequestedResolution = new Vector2Int(w, h);
                    pca.MaxFramerate = fps;
                    pca.enabled = true;
                }
                catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
                if (err != null) _log.Emit("pca_config_error", x => x.Prop("profile", profile).Prop("eye", probe.Eye).Prop("created", created).Prop("error", err));
            }
            // Wait for IsPlaying on the wanted eyes.
            double deadline = Time.realtimeSinceStartupAsDouble + PcaStartTimeoutSeconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                bool all = true;
                for (int e = 0; e < 2; e++) if (_eyes[e].Wanted && !(_eyes[e].Pca != null && _eyes[e].Pca.IsPlaying)) all = false;
                if (all) break;
                yield return null;
            }
            for (int e = 0; e < 2; e++)
            {
                var probe = _eyes[e]; if (!probe.Wanted) continue;
                var pca = probe.Pca;
                bool playing = pca != null && pca.IsPlaying;
                _log.Emit("pca_start", x =>
                {
                    x.Prop("profile", profile).Prop("eye", probe.Eye).Prop("playing", playing).Prop("enabled", pca != null && pca.enabled)
                     .BeginObject("requested").Prop("w", w).Prop("h", h).Prop("fps", fps).EndObject();
                    if (!playing) return;
                    x.BeginObject("current").Prop("w", pca.CurrentResolution.x).Prop("h", pca.CurrentResolution.y).EndObject();
                    var i = pca.Intrinsics;
                    x.BeginObject("intrinsics").Prop("fx", i.FocalLength.x).Prop("fy", i.FocalLength.y).Prop("cx", i.PrincipalPoint.x).Prop("cy", i.PrincipalPoint.y)
                     .Prop("sensorW", i.SensorResolution.x).Prop("sensorH", i.SensorResolution.y)
                     .BeginObject("lensOffset").Prop("px", i.LensOffset.position.x).Prop("py", i.LensOffset.position.y).Prop("pz", i.LensOffset.position.z)
                     .Prop("qx", i.LensOffset.rotation.x).Prop("qy", i.LensOffset.rotation.y).Prop("qz", i.LensOffset.rotation.z).Prop("qw", i.LensOffset.rotation.w).EndObject().EndObject();
                    try
                    {
                        var t = pca.GetTexture();
                        if (t != null) x.BeginObject("texture").Prop("w", t.width).Prop("h", t.height).Prop("type", t.GetType().Name).Prop("format", t.graphicsFormat.ToString()).Prop("dimension", t.dimension.ToString()).EndObject();
                    }
                    catch (Exception ex) { x.Prop("textureError", ex.Message); }
                });
            }
            bool any = false; for (int e = 0; e < 2; e++) if (_eyes[e].Wanted && _eyes[e].Pca != null && _eyes[e].Pca.IsPlaying) any = true;
            if (!any) { _summary.Add(profile + "=nostart"); yield break; }

            for (int e = 0; e < 2; e++) _eyes[e].Reset();
            _ringLn = _ringRn = 0; _pairAbsMs.Reset(); _pairSignedMs.Reset(); _pairSamples = 0;
            _pcaSampling = true;
            yield return Wait(seconds);
            _pcaSampling = false;

            EmitPcaProfile(profile, seconds, w, h, fps);
        }

        void SamplePca()
        {
            DateTime utcNow = DateTime.UtcNow;
            long monoNow = MonoNs(); double ovrNow = OvrNowSeconds();
            double now = Time.realtimeSinceStartupAsDouble;
            for (int e = 0; e < 2; e++)
            {
                var probe = _eyes[e]; if (probe == null || !probe.Wanted) continue;
                var pca = probe.Pca; if (pca == null || !pca.IsPlaying || !pca.IsUpdatedThisFrame) continue;
                long ticks = pca.Timestamp.Ticks;
                if (ticks == probe.LastTsTicks) continue;
                long monoTs = ReadPcaMonoNs(pca); if (monoTs < 0) probe.MonoFieldMissing++;
                if (probe.Frames > 0)
                {
                    double dMs = (ticks - probe.LastTsTicks) / 1e4;
                    if (dMs <= 0) probe.NonMonotonic++; else probe.DeltaMs.Add(dMs);
                    probe.UnityFramesBetween.Add(Time.frameCount - probe.LastUnityFrame);
                }
                else probe.FirstUnity = now;
                probe.LatencyUtcMs.Add((utcNow.Ticks - ticks) / 1e4);
                if (monoTs > 0 && monoNow > 0) probe.LatencyMonoMs.Add((monoNow - monoTs) / 1e6);
                if (monoTs > 0 && ovrNow > 0) probe.LatencyOvrMs.Add((ovrNow * 1e9 - monoTs) / 1e6);
                try
                {
                    Pose pose = pca.GetCameraPose();
                    if (probe.HavePose) { probe.PosDeltaMm.Add(Vector3.Distance(pose.position, probe.LastPose.position) * 1000.0); probe.RotDeltaDeg.Add(Quaternion.Angle(pose.rotation, probe.LastPose.rotation)); }
                    probe.LastPose = pose; probe.HavePose = true;
                }
                catch (Exception) { }
                if (probe.TraceTsUs.Count < TraceFramesPerEye) { probe.TraceTsUs.Add((ticks - ClockDomains.UnixEpochTicks) / 10); probe.TraceMonoNs.Add(monoTs); probe.TraceUnityMs.Add((now - _phaseStart) * 1000.0); }
                probe.Frames++; probe.LastTsTicks = ticks; probe.LastMonoNs = monoTs; probe.LastUnity = now; probe.LastUnityFrame = Time.frameCount;
                // pairing: nearest timestamp of the other eye seen so far
                if (e == 0) { Push(_ringL, ref _ringLn, ticks); Pair(ticks, _ringR, _ringRn, +1); }
                else { Push(_ringR, ref _ringRn, ticks); Pair(ticks, _ringL, _ringLn, -1); }
            }
        }

        static void Push(long[] ring, ref int n, long v) { ring[n % ring.Length] = v; n++; }

        void Pair(long ticks, long[] other, int otherN, int sign)
        {
            if (otherN == 0) return;
            long best = long.MaxValue; int count = Math.Min(otherN, other.Length);
            for (int i = 0; i < count; i++) { long d = ticks - other[i]; if (Math.Abs(d) < Math.Abs(best)) best = d; }
            _pairAbsMs.Add(Math.Abs(best) / 1e4); _pairSignedMs.Add(sign * best / 1e4); _pairSamples++;
        }

        void EmitPcaProfile(string profile, float seconds, int w, int h, int fps)
        {
            string sum = profile + ":";
            _log.Emit("pca_profile", x =>
            {
                x.Prop("profile", profile).Prop("seconds", seconds).BeginObject("requested").Prop("w", w).Prop("h", h).Prop("fps", fps).EndObject();
                x.Prop("timestampType", "DateTime(UnixEpoch+micros, CLOCK_REALTIME); monotonicNs via reflection of PassthroughCameraAccess._timestampNsMonotonic (XrTime base)");
                x.BeginObject("eyes");
                for (int e = 0; e < 2; e++)
                {
                    var p = _eyes[e]; if (p == null || !p.Wanted) continue;
                    double dur = p.LastUnity - p.FirstUnity;
                    double efps = p.Frames > 1 && dur > 0 ? (p.Frames - 1) / dur : 0;
                    x.BeginObject(p.Eye).Prop("frames", p.Frames).Prop("deliveredFps", efps).Prop("nonMonotonic", p.NonMonotonic).Prop("monoFieldMissing", p.MonoFieldMissing)
                     .Prop("playing", p.Pca != null && p.Pca.IsPlaying);
                    if (p.Pca != null && p.Pca.IsPlaying) x.Prop("currentW", p.Pca.CurrentResolution.x).Prop("currentH", p.Pca.CurrentResolution.y);
                    p.DeltaMs.WriteTo(x, "deltaMs"); p.DeltaMs.WriteHistogram(x, "deltaHistMs", 2.0, 60);
                    p.LatencyUtcMs.WriteTo(x, "latencyUtcMs"); p.LatencyMonoMs.WriteTo(x, "latencyMonoMs"); p.LatencyOvrMs.WriteTo(x, "latencyOvrMs");
                    p.PosDeltaMm.WriteTo(x, "poseDeltaMm"); p.RotDeltaDeg.WriteTo(x, "poseDeltaDeg"); p.UnityFramesBetween.WriteTo(x, "unityFramesBetween");
                    x.EndObject();
                    sum += p.Eye + "=" + efps.ToString("F1") + " ";
                }
                x.EndObject();
                x.BeginObject("pairing").Prop("samples", _pairSamples);
                _pairAbsMs.WriteTo(x, "absMs"); _pairSignedMs.WriteTo(x, "signedMs"); _pairAbsMs.WriteHistogram(x, "absHistMs", 2.0, 40);
                x.EndObject();
            });
            for (int e = 0; e < 2; e++)
            {
                var p = _eyes[e]; if (p == null || !p.Wanted || p.TraceTsUs.Count == 0) continue;
                _log.Emit("pca_trace", x =>
                {
                    x.Prop("profile", profile).Prop("eye", p.Eye).Prop("n", p.TraceTsUs.Count);
                    x.BeginArray("tsUs").Values(p.TraceTsUs).EndArray();
                    x.BeginArray("monoNs").Values(p.TraceMonoNs).EndArray();
                    x.BeginArray("unityMs").Values(p.TraceUnityMs).EndArray();
                });
            }
            if (_pairSamples > 0) sum += "pair=" + _pairAbsMs.Percentile(50).ToString("F1") + "ms";
            _summary.Add(sum);
        }

        void StopPcaSampling() { _pcaSampling = false; }

        void StopAllPca()
        {
            StopPcaSampling();
            for (int e = 0; e < 2; e++)
            {
                var p = _eyes[e]; if (p == null) continue; p.Wanted = false;
                try { if (p.Pca != null && p.Pca.enabled) p.Pca.enabled = false; } catch (Exception ex) { _log.Error("StopAllPca", ex); }
            }
        }

        static bool SafeIsSupported() { try { return PassthroughCameraAccess.IsSupported; } catch (Exception) { return false; } }

        // ------------------------------------------------------------------ camera2
        IEnumerator Camera2Phase(string mode, bool withPca)
        {
            if (!FinalScanNative.Available)
            {
                _log.Emit("camera2", x => x.Prop("mode", mode).Prop("available", false).Prop("reason", "native plugin unavailable"));
                yield break;
            }
            if (withPca)
            {
                // Same as pca_30_960 but no measurement emission: we only need the cameras open.
                for (int e = 0; e < 2; e++)
                {
                    var pos = e == 0 ? PassthroughCameraAccess.CameraPositionType.Left : PassthroughCameraAccess.CameraPositionType.Right;
                    var probe = _eyes[e] ?? (_eyes[e] = new PcaEyeProbe { Eye = e == 0 ? "L" : "R" });
                    try
                    {
                        if (probe.Pca == null) probe.Pca = FindOrCreatePca(pos, out _);
                        if (probe.Pca.enabled) probe.Pca.enabled = false;
                        probe.Pca.RequestedResolution = new Vector2Int(1280, 960); probe.Pca.MaxFramerate = 30; probe.Pca.enabled = true;
                    }
                    catch (Exception ex) { _log.Error("Camera2Phase pca", ex); }
                }
                double deadline = Time.realtimeSinceStartupAsDouble + PcaStartTimeoutSeconds;
                while (Time.realtimeSinceStartupAsDouble < deadline && !(_eyes[0].Pca != null && _eyes[0].Pca.IsPlaying && _eyes[1].Pca != null && _eyes[1].Pca.IsPlaying)) yield return null;
            }
            bool pcaL = _eyes[0]?.Pca != null && _eyes[0].Pca.IsPlaying, pcaR = _eyes[1]?.Pca != null && _eyes[1].Pca.IsPlaying;
            int startRc = int.MinValue, stopRc = int.MinValue; string report = null, err = null;
            try { startRc = FinalScanNative.CameraProbeStart(1280, 960, 30); } catch (Exception e) { err = e.Message; }
            _log.Emit("camera2_start", x => x.Prop("mode", mode).Prop("startRc", startRc).Prop("pcaLeftPlaying", pcaL).Prop("pcaRightPlaying", pcaR).Prop("error", err));
            yield return Wait(Camera2Seconds);
            // Count PCA frames that still arrive while camera2 is open (exclusivity evidence).
            int pcaFramesDuring = -1;
            if (withPca) { pcaFramesDuring = 0; for (int e = 0; e < 2; e++) if (_eyes[e].Pca != null && _eyes[e].Pca.IsPlaying) pcaFramesDuring++; }
            try { stopRc = FinalScanNative.CameraProbeStop(); report = FinalScanNative.CameraProbeReportJson(); } catch (Exception e) { err = (err ?? "") + " | " + e.Message; }
            _log.Emit("camera2", x => x.Prop("mode", mode).Prop("available", true).Prop("startRc", startRc).Prop("stopRc", stopRc).Prop("seconds", Camera2Seconds)
                .Prop("pcaLeftPlayingBefore", pcaL).Prop("pcaRightPlayingBefore", pcaR).Prop("pcaEyesPlayingAfter", pcaFramesDuring).Prop("error", err)
                .PropRaw("report", IsJsonObject(report) ? report : null).Prop("reportRaw", IsJsonObject(report) ? null : report));
            _summary.Add("cam2_" + mode + "=rc" + startRc);
        }

        // ------------------------------------------------------------------ vulkan
        sealed class FrameSegment
        {
            public string Name; public bool FrameMark;
            public readonly SampleStats FrameMs = new SampleStats(), GpuMs = new SampleStats(), CpuMs = new SampleStats(), MainMs = new SampleStats(), RenderMs = new SampleStats();
            public readonly Dictionary<string, SampleStats> XrStats = new Dictionary<string, SampleStats>();
            public int Frames; public bool FtEnabled;
            public void Tick(DeviceProbeHost h)
            {
                Frames++;
                FrameMs.Add(Time.unscaledDeltaTime * 1000.0);
                if (FrameMark) h.IssueRenderEvent(FinalScanNative.RenderEvent.ProbeFrameMark);
                try
                {
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, h._ft) > 0)
                    {
                        FtEnabled = true; var t = h._ft[0];
                        GpuMs.Add(t.gpuFrameTime); CpuMs.Add(t.cpuFrameTime); MainMs.Add(t.cpuMainThreadFrameTime); RenderMs.Add(t.cpuRenderThreadFrameTime);
                    }
                }
                catch (Exception) { }
                h.SampleXrStats(this);
            }
            public void Write(JsonWriter w)
            {
                w.Prop("segment", Name).Prop("frames", Frames).Prop("frameTimingManager", FtEnabled);
                FrameMs.WriteTo(w, "frameMs"); FrameMs.WriteHistogram(w, "frameHistMs", 1.0, 40);
                GpuMs.WriteTo(w, "gpuMs"); CpuMs.WriteTo(w, "cpuMs"); MainMs.WriteTo(w, "mainThreadMs"); RenderMs.WriteTo(w, "renderThreadMs");
                w.BeginObject("xrStats"); foreach (var kv in XrStats) kv.Value.WriteTo(w, kv.Key); w.EndObject();
            }
        }

        void SampleXrStats(FrameSegment seg)
        {
            if (_displays.Count == 0) { try { SubsystemManager.GetSubsystems(_displays); } catch (Exception) { } if (_displays.Count == 0) return; }
            var d = _displays[0];
            foreach (var name in _xrStatNames)
            {
                try
                {
                    if (UnityEngine.XR.Provider.XRStats.TryGetStat(d, name, out float v))
                    {
                        if (!seg.XrStats.TryGetValue(name, out var s)) seg.XrStats[name] = s = new SampleStats(4096);
                        s.Add(v);
                    }
                }
                catch (Exception) { }
            }
        }

        void IssueRenderEvent(FinalScanNative.RenderEvent e)
        {
            if (_renderEventFunc == IntPtr.Zero) return;
            if (_cb == null) _cb = new CommandBuffer { name = "FS-PROBE" };
            _cb.Clear();
            _cb.IssuePluginEvent(_renderEventFunc, (int)e);
            Graphics.ExecuteCommandBuffer(_cb);
        }

        IEnumerator VulkanPhase()
        {
            if (!FinalScanNative.Available || !FinalScanNative.VulkanReady)
            {
                _log.Emit("vulkan_skip", x => x.Prop("available", FinalScanNative.Available).Prop("vulkanReady", FinalScanNative.VulkanReady).Prop("graphicsApi", SystemInfo.graphicsDeviceType.ToString()));
                _summary.Add("vulkan=skip");
                yield break;
            }
            try { _renderEventFunc = FinalScanNative.RenderEventFunc; } catch (Exception e) { _log.Error("RenderEventFunc", e); yield break; }
            if (_renderEventFunc == IntPtr.Zero) { _log.Emit("vulkan_skip", x => x.Prop("reason", "RenderEventFunc null")); yield break; }

            IssueRenderEvent(FinalScanNative.RenderEvent.ProbeEmptySubmit);
            yield return Wait(1f);
            _log.Emit("vulkan_step", x => x.Prop("step", "empty_submit").PropRaw("results", SafeProbeResults()));
            IssueRenderEvent(FinalScanNative.RenderEvent.ProbeTimestamp);
            yield return Wait(1f);
            _log.Emit("vulkan_step", x => x.Prop("step", "timestamp").PropRaw("results", SafeProbeResults()));
            IssueRenderEvent(FinalScanNative.RenderEvent.ProbePipelineBinary);
            yield return Wait(2f);
            _log.Emit("vulkan_step", x => x.Prop("step", "pipeline_binary").PropRaw("results", SafeProbeResults()));

            // Frame-time segments: pre (marks only) -> overlap (scanner queue busy) -> post (marks only).
            _frames = new FrameSegment { Name = "pre", FrameMark = true };
            yield return Wait(VulkanPreBaselineSeconds);
            var pre = _frames; _frames = null; _log.Emit("vulkan_frames", pre.Write);

            IssueRenderEvent(FinalScanNative.RenderEvent.ProbeOverlapStart);
            yield return null;
            _frames = new FrameSegment { Name = "overlap", FrameMark = true };
            yield return Wait(VulkanOverlapSeconds);
            var ov = _frames; _frames = null;
            IssueRenderEvent(FinalScanNative.RenderEvent.ProbeOverlapStop);
            yield return null; yield return null;
            _log.Emit("vulkan_frames", ov.Write);

            _frames = new FrameSegment { Name = "post", FrameMark = true };
            yield return Wait(VulkanPostBaselineSeconds);
            var post = _frames; _frames = null; _log.Emit("vulkan_frames", post.Write);

            yield return null;
            _log.Emit("vulkan_probe", x => x.PropRaw("results", SafeProbeResults()).PropRaw("deviceReport", SafeDeviceReport()));
            _summary.Add("vk pre/ovl/post p99=" + pre.FrameMs.Percentile(99).ToString("F1") + "/" + ov.FrameMs.Percentile(99).ToString("F1") + "/" + post.FrameMs.Percentile(99).ToString("F1") + "ms");
        }

        string SafeProbeResults() { try { var s = FinalScanNative.ProbeResultsJson(); return IsJsonObject(s) ? s : JsonWriter.Escape(s ?? ""); } catch (Exception e) { return JsonWriter.Escape("error: " + e.Message); } }
        string SafeDeviceReport() { try { var s = FinalScanNative.DeviceReportJson(); return IsJsonObject(s) ? s : JsonWriter.Escape(s ?? ""); } catch (Exception e) { return JsonWriter.Escape("error: " + e.Message); } }

        // ------------------------------------------------------------------ thermal / memory / clocks
        IEnumerator ThermalPhase()
        {
            EmitThermal("thermal_snapshot");
            yield return null;
        }

        IEnumerator DonePhase()
        {
            string summary = string.Join(" | ", _summary);
            _log.Emit("done", w =>
            {
                w.Prop("summary", summary).Prop("runSeconds", Time.realtimeSinceStartupAsDouble - _runStart).Prop("jsonl", _log.FilePath);
                WriteClockFits(w);
            });
            _log.Flush();
            _phase = "done";
            UpdateStatusText();
            yield break;
        }

        IEnumerator SamplerLoop()
        {
            while (true)
            {
                EmitMemory();
                EmitThermal("thermal");
                EmitClockTriplet();
                if (_phase == "done") yield break;
                yield return new WaitForSecondsRealtime(SamplerIntervalSeconds);
            }
        }

        void EmitClockTriplet()
        {
            try
            {
                long m0 = MonoNs(); double ovr = OvrNowSeconds(); long boot = BootNs(); long utc = DateTime.UtcNow.Ticks; long m1 = MonoNs();
                var t = new ClockTriplet { ovrSeconds = ovr, monotonicNs = m0 > 0 && m1 > 0 ? (m0 + m1) / 2 : m0, boottimeNs = boot, utcTicks = utc };
                if (t.ovrSeconds > 0 && t.monotonicNs > 0) _triplets.Add(t);
                double unity = Time.realtimeSinceStartupAsDouble;
                _log.Emit("clock_triplet", w => w.Prop("ovrSeconds", t.ovrSeconds).Prop("monotonicNs", t.monotonicNs).Prop("boottimeNs", t.boottimeNs)
                    .Prop("utcTicks", t.utcTicks).Prop("unixSeconds", t.UnixSeconds).Prop("bracketNs", m1 - m0).Prop("unitySeconds", unity)
                    .Prop("ovrMinusMonoMs", (t.ovrSeconds - t.MonotonicSeconds) * 1000.0).Prop("bootMinusMonoMs", (t.BoottimeSeconds - t.MonotonicSeconds) * 1000.0));
            }
            catch (Exception e) { _log.Error("EmitClockTriplet", e); }
        }

        void WriteClockFits(JsonWriter w)
        {
            w.BeginObject("clockFits").Prop("samples", _triplets.Count);
            WriteFit(w, "ovrToMono", ClockDomains.Fit(_triplets, ClockAxis.OvrSeconds, ClockAxis.MonotonicNs));
            WriteFit(w, "monoToBoot", ClockDomains.Fit(_triplets, ClockAxis.MonotonicNs, ClockAxis.BoottimeNs));
            WriteFit(w, "monoToUtc", ClockDomains.Fit(_triplets, ClockAxis.MonotonicNs, ClockAxis.UtcTicks));
            WriteFit(w, "ovrToUtc", ClockDomains.Fit(_triplets, ClockAxis.OvrSeconds, ClockAxis.UtcTicks));
            w.EndObject();
        }

        static void WriteFit(JsonWriter w, string key, LinearFit f)
        {
            w.BeginObject(key).Prop("valid", f.valid).Prop("n", f.count).Prop("slope", f.slope).Prop("offsetSeconds", f.offset).Prop("driftPpm", f.DriftPpm)
             .Prop("residualRmsMs", f.residualRms * 1000.0).Prop("residualMaxMs", f.residualMax * 1000.0).EndObject();
        }

        static double OvrNowSeconds() { try { return OVRPlugin.initialized ? OVRPlugin.GetTimeInSeconds() : -1; } catch (Exception) { return -1; } }
        static long MonoNs() { try { return FinalScanNative.Available ? FinalScanNative.MonotonicNowNs() : -1; } catch (Exception) { return -1; } }
        static long BootNs() { try { return FinalScanNative.Available ? FinalScanNative.BoottimeNowNs() : -1; } catch (Exception) { return -1; } }

        void EmitMemory()
        {
            try
            {
                long total = Profiler.GetTotalAllocatedMemoryLong(), reserved = Profiler.GetTotalReservedMemoryLong(), gfx = Profiler.GetAllocatedMemoryForGraphicsDriver(), mono = Profiler.GetMonoUsedSizeLong();
                _log.Emit("memory", w =>
                {
                    w.Prop("phaseName", _phase).Prop("phaseElapsed", Time.realtimeSinceStartupAsDouble - _phaseStart)
                     .Prop("unityAllocatedMB", total / 1048576.0).Prop("unityReservedMB", reserved / 1048576.0).Prop("unityGfxDriverMB", gfx / 1048576.0).Prop("monoUsedMB", mono / 1048576.0)
                     .Prop("systemMemoryMB", SystemInfo.systemMemorySize);
                    WriteAndroidMemory(w);
                });
            }
            catch (Exception e) { _log.Error("EmitMemory", e); }
        }

        void EmitThermal(string evt)
        {
            try
            {
                _log.Emit(evt, w =>
                {
                    w.Prop("phaseName", _phase);
                    w.BeginObject("ovr");
                    bool init = false; try { init = OVRPlugin.initialized; } catch (Exception) { }
                    w.Prop("initialized", init);
                    if (init)
                    {
                        Try(w, "batteryTemperatureC", () => OVRPlugin.batteryTemperature);
                        Try(w, "batteryLevel", () => OVRPlugin.batteryLevel);
                        Try(w, "batteryStatus", () => OVRPlugin.batteryStatus.ToString());
                        Try(w, "powerSaving", () => OVRPlugin.powerSaving);
                        Try(w, "cpuLevel", () => OVRPlugin.cpuLevel);
                        Try(w, "gpuLevel", () => OVRPlugin.gpuLevel);
                        Try(w, "suggestedCpuPerfLevel", () => OVRPlugin.suggestedCpuPerfLevel.ToString());
                        Try(w, "suggestedGpuPerfLevel", () => OVRPlugin.suggestedGpuPerfLevel.ToString());
                        Try(w, "displayFrequency", () => OVRPlugin.systemDisplayFrequency);
                        Try(w, "appFramerate", () => OVRPlugin.GetAppFramerate());
                    }
                    w.EndObject();
                    w.BeginObject("unity");
                    Try(w, "batteryLevel", () => SystemInfo.batteryLevel);
                    Try(w, "batteryStatus", () => SystemInfo.batteryStatus.ToString());
                    w.EndObject();
                    WriteAndroidThermal(w);
                });
            }
            catch (Exception e) { _log.Error("EmitThermal", e); }
        }

        static void Try(JsonWriter w, string key, Func<float> f) { try { w.Prop(key, f()); } catch (Exception e) { w.Prop(key + "Error", e.GetType().Name); } }
        static void Try(JsonWriter w, string key, Func<int> f) { try { w.Prop(key, f()); } catch (Exception e) { w.Prop(key + "Error", e.GetType().Name); } }
        static void Try(JsonWriter w, string key, Func<bool> f) { try { w.Prop(key, f()); } catch (Exception e) { w.Prop(key + "Error", e.GetType().Name); } }
        static void Try(JsonWriter w, string key, Func<string> f) { try { w.Prop(key, f()); } catch (Exception e) { w.Prop(key + "Error", e.GetType().Name); } }

        // ------------------------------------------------------------------ Android JNI (guarded)
        void InitAndroid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_androidInit) return; _androidInit = true;
            try
            {
                using (var at = new AndroidJavaClass("android.app.ActivityThread")) _context = at.CallStatic<AndroidJavaObject>("currentApplication");
                _activityManager = _context?.Call<AndroidJavaObject>("getSystemService", "activity");
                _powerManager = _context?.Call<AndroidJavaObject>("getSystemService", "power");
                using (var proc = new AndroidJavaClass("android.os.Process")) _pid = proc.CallStatic<int>("myPid");
                _debugClass = new AndroidJavaClass("android.os.Debug");
            }
            catch (Exception e) { _log.Error("InitAndroid", e); }
#endif
        }

        void WriteAndroidBuild(JsonWriter w)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            w.BeginObject("android");
            try
            {
                using var v = new AndroidJavaClass("android.os.Build$VERSION");
                w.Prop("release", v.GetStatic<string>("RELEASE")).Prop("sdkInt", v.GetStatic<int>("SDK_INT")).Prop("incremental", v.GetStatic<string>("INCREMENTAL"));
                using var b = new AndroidJavaClass("android.os.Build");
                w.Prop("model", b.GetStatic<string>("MODEL")).Prop("device", b.GetStatic<string>("DEVICE")).Prop("hardware", b.GetStatic<string>("HARDWARE")).Prop("fingerprint", b.GetStatic<string>("FINGERPRINT"));
            }
            catch (Exception e) { w.Prop("error", e.Message); }
            try { using var vros = new AndroidJavaClass("vros.os.VrosBuild"); w.Prop("vrosSdkVersion", vros.CallStatic<int>("getSdkVersion")); }
            catch (Exception e) { w.Prop("vrosError", e.GetType().Name); }
            w.Prop("pcaIsSupported", SafeIsSupported());
            w.EndObject();
#else
            w.PropNull("android");
#endif
        }

        void WriteAndroidMemory(JsonWriter w)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InitAndroid();
            try { if (_debugClass != null) w.Prop("debugPssKB", _debugClass.CallStatic<long>("getPss")); } catch (Exception e) { w.Prop("debugPssError", e.GetType().Name); }
            try
            {
                if (_activityManager == null) { w.Prop("activityManager", false); return; }
                var infos = _activityManager.Call<AndroidJavaObject[]>("getProcessMemoryInfo", new int[] { _pid });
                if (infos == null || infos.Length == 0) { w.Prop("memoryInfo", false); return; }
                using var mi = infos[0];
                w.Prop("totalPssKB", mi.Call<int>("getTotalPss")).Prop("totalPrivateDirtyKB", mi.Call<int>("getTotalPrivateDirty")).Prop("totalSharedDirtyKB", mi.Call<int>("getTotalSharedDirty"));
                w.Prop("nativePssKB", mi.Get<int>("nativePss")).Prop("dalvikPssKB", mi.Get<int>("dalvikPss")).Prop("otherPssKB", mi.Get<int>("otherPss"));
                string[] stats = { "summary.java-heap", "summary.native-heap", "summary.code", "summary.stack", "summary.graphics", "summary.private-other", "summary.system", "summary.total-pss", "summary.total-swap" };
                foreach (var s in stats)
                {
                    try { string v = mi.Call<string>("getMemoryStat", s); if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long kb)) w.Prop(s.Replace("summary.", "").Replace("-", "_") + "KB", kb); }
                    catch (Exception) { }
                }
                for (int i = 1; i < infos.Length; i++) infos[i]?.Dispose();
            }
            catch (Exception e) { w.Prop("memoryInfoError", e.GetType().Name + ": " + e.Message); }
#endif
        }

        void WriteAndroidThermal(JsonWriter w)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InitAndroid();
            w.BeginObject("android");
            try { if (_powerManager != null) w.Prop("thermalStatus", _powerManager.Call<int>("getCurrentThermalStatus")); } catch (Exception e) { w.Prop("thermalStatusError", e.GetType().Name); }
            try { if (_powerManager != null) w.Prop("thermalHeadroom10s", _powerManager.Call<float>("getThermalHeadroom", 10)); } catch (Exception e) { w.Prop("thermalHeadroomError", e.GetType().Name); }
            try { if (_powerManager != null) w.Prop("sustainedPerfSupported", _powerManager.Call<bool>("isSustainedPerformanceModeSupported")); } catch (Exception) { }
            w.BeginArray("zones");
            for (int i = 0; i < 40; i++)
            {
                string tp = "/sys/class/thermal/thermal_zone" + i + "/type", tt = "/sys/class/thermal/thermal_zone" + i + "/temp";
                try
                {
                    if (!File.Exists(tt)) break;
                    string type = File.Exists(tp) ? File.ReadAllText(tp).Trim() : ("zone" + i);
                    string temp = File.ReadAllText(tt).Trim();
                    if (long.TryParse(temp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long mC)) w.BeginObject().Prop("type", type).Prop("milliC", mC).EndObject();
                }
                catch (Exception) { break; }
            }
            w.EndArray();
            w.EndObject();
#else
            w.PropNull("android");
#endif
        }

        // ------------------------------------------------------------------ status text
        static Camera MainCamera()
        {
            var c = Camera.main;
            if (c == null) { var all = Camera.allCameras; if (all != null && all.Length > 0) c = all[0]; }
            return c;
        }

        void CreateStatusText()
        {
            try
            {
                var cam = MainCamera();
                var go = new GameObject("[FinalScan] ProbeStatus");
                if (cam != null) { go.transform.SetParent(cam.transform, false); go.transform.localPosition = new Vector3(0f, -0.05f, 1.5f); go.transform.localRotation = Quaternion.identity; }
                else go.transform.position = new Vector3(0f, 1.5f, 1.5f);
                _status = go.AddComponent<TextMesh>();
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) { _status.font = font; var mr = go.GetComponent<MeshRenderer>(); if (mr != null) mr.material = font.material; }
                _status.fontSize = 48; _status.characterSize = 0.0045f; _status.anchor = TextAnchor.MiddleCenter; _status.alignment = TextAlignment.Center; _status.color = Color.white;
                _status.text = "FinalScan probe";
            }
            catch (Exception e) { _log.Error("CreateStatusText", e); }
        }

        void UpdateStatusText()
        {
            if (_status == null) return;
            var sb = new System.Text.StringBuilder(256);
            sb.Append("FS-PROBE ").Append(_phase).Append("  ").Append((Time.realtimeSinceStartupAsDouble - _phaseStart).ToString("F0")).Append(" s  (run ").Append((Time.realtimeSinceStartupAsDouble - _runStart).ToString("F0")).Append(" s)\n");
            if (_pcaSampling)
            {
                for (int e = 0; e < 2; e++)
                {
                    var p = _eyes[e]; if (p == null || !p.Wanted) continue;
                    double dur = p.LastUnity - p.FirstUnity;
                    sb.Append(p.Eye).Append(": ").Append(p.Frames).Append(" fr ").Append(dur > 0 && p.Frames > 1 ? ((p.Frames - 1) / dur).ToString("F1") : "-").Append(" fps  dt p50 ").Append(p.DeltaMs.Percentile(50).ToString("F1")).Append(" ms  lat ").Append(p.LatencyUtcMs.Percentile(50).ToString("F0")).Append(" ms  nonmono ").Append(p.NonMonotonic).Append('\n');
                }
                if (_pairSamples > 0) sb.Append("pair |dt| p50 ").Append(_pairAbsMs.Percentile(50).ToString("F1")).Append(" ms\n");
            }
            if (_depthSubscribed) sb.Append("depth frames ").Append(_depth.Frames).Append(" dt p50 ").Append(_depth.DeltaMs.Percentile(50).ToString("F1")).Append(" ms age ").Append(_depth.AgeOvrMs.Percentile(50).ToString("F0")).Append(" ms\n");
            if (_frames != null) sb.Append("frame ").Append(_frames.Name).Append(" p50 ").Append(_frames.FrameMs.Percentile(50).ToString("F1")).Append(" p99 ").Append(_frames.FrameMs.Percentile(99).ToString("F1")).Append(" ms\n");
            int start = Math.Max(0, _summary.Count - 6);
            for (int i = start; i < _summary.Count; i++) sb.Append(_summary[i]).Append('\n');
            _status.text = sb.ToString();
        }
    }
}
