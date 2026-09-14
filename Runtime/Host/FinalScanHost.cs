using System;
using FinalScan.Platform.Native;
using FinalScan.Platform.Sensor;
using FinalScan.Render;
using FinalScan.Telemetry;
using FinalScan.World;
using UnityEngine;
using RenderMode = FinalScan.Render.RenderMode;

namespace FinalScan.Host
{
    /// <summary>
    /// C02 host binding on the "[FinalScan]" root: lifecycle of the native executor (FsHost_Init/Shutdown), status
    /// polling, per-frame scheduler hints, stereo view + depth priors for the native cull (through
    /// <see cref="FinalScanRenderFeature"/>), GPU headroom, render mode, and the 2 s telemetry line
    /// <c>FS-HOST-CS {json}</c>. Residency centre updates live in <see cref="FinalScan.Residency.ResidencyDriver"/>
    /// (position only); nothing here passes orientation to residency.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class FinalScanHost : MonoBehaviour, IRenderFrameHost
    {
        public const string TelemetryPrefix = HostLog.HostPrefix;   // FS-HOST-CS

        public static FinalScanHost Current { get; private set; }

        [Header("FsHostConfig (provisional, contract §15 / finalscan_host_api.h)")]
        [SerializeField] uint residentPages = 256;
        [Tooltip("Canonical surfel pool (slabs of 256). 4 M = 128 MiB surfels + 32 MiB evidence.")]
        [SerializeField] uint canonicalSurfels = 4194304;
        [SerializeField] uint indexLeaves = 1048576;
        [SerializeField] uint indexNodes = 262144;
        [SerializeField] uint renderBlocks = 65536;
        [SerializeField] uint renderNodes = 524288;
        [SerializeField] uint pageHashCapacity = 4096;
        [SerializeField] uint measurementRingCapacity = 1 << 18;
        [SerializeField] uint drawRecordCapacity = 1 << 20;
        [SerializeField] uint frameBudgetUs = 2500;
        [Tooltip("PUBLISH, SCAN, INNER_RESIDENCY, APPEARANCE, COLD")]
        [SerializeField] uint[] inFlightMax = { 4, 2, 2, 1, 1 };
        [Tooltip("PUBLISH, SCAN, INNER_RESIDENCY, APPEARANCE, COLD (microseconds)")]
        [SerializeField] uint[] quantumUs = { 500, 2000, 2000, 1500, 1000 };
        [SerializeField] uint useScannerQueue = 1;

        [Header("Synthetic world (C04/C07 acceptance fixture only)")]
        [Tooltip("Never enabled by default. Only `setprop debug.finalscan.synthetic <kind+1>` (read once at start) enables it; the normal run is the LIVE sensor path.")]
        [SerializeField] uint enableSyntheticWorld = 0;
        [Tooltip("Set from the property (value - 1); 0 room box, 1 corridor, 2 stairs, 3 thin wall, 4 dense.")]
        [SerializeField] int syntheticKind = -1;
        [SerializeField] float syntheticExtentM = 6f;
        [SerializeField] float syntheticSurfelSpacingM = 0.02f;
        [SerializeField] uint syntheticSeed = 1;

        [Header("Render (§13.5)")]
        [SerializeField] RenderMode renderMode = RenderMode.Scan;
        [SerializeField] float fovealErrorPx = 1.0f;
        [SerializeField] float peripheralErrorPx = 3.5f;
        [SerializeField] float predictionMarginDeg = 5f;
        [Tooltip("Extra latency added to 'now + display period' for the predicted display time hint.")]
        [SerializeField] float extraDisplayLatencyMs = 0f;

        [Header("Telemetry")]
        [SerializeField] float telemetryIntervalSec = 2f;
        [SerializeField] float propPollIntervalSec = 2f;

        HostLog _log;
        bool _native;
        bool _initCalled;
        int _initResult;
        FinalScanHostNative.HostStatus _status = FinalScanHostNative.HostStatus.Uninitialized;
        FinalScanHostNative.HostStatus _lastLoggedStatus = (FinalScanHostNative.HostStatus)(-1);
        bool _syntheticRequested;
        int _syntheticResult;
        bool _lodPolicySent;
        uint _lodBudgetSent;
        bool _modeSent;
        RenderMode _modeSentValue;
        IntPtr _renderEventFunc;
        float _nextTelemetry, _nextPropPoll;
        readonly FrameTiming[] _frameTiming = new FrameTiming[1];
        readonly float[] _headPos = new float[3], _headVel = new float[3], _headRot = new float[4];
        readonly float[] _headPosView = new float[3];
        Vector3 _prevHeadPos;
        bool _havePrevHead;
        int _prevDepthFrame = -1;
        int _prevDepthResult;
        long _telemetryLines;
        readonly long[] _zone = new long[FinalScanHostNative.ZoneStatsCount];
        readonly long[] _cull = new long[FinalScanHostNative.CullStatsCount];
        readonly long[] _cls = new long[FinalScanHostNative.ClassStatsCount];
        EnvDepthFeeder _envDepth;
        SensorAuthority _sensor;

        // ---- state exposed to HUD / residency / spin test -------------------------------------------------------
        public bool NativeAvailable => _native;
        public int ReportedAbi => FinalScanHostNative.ReportedAbiVersion;
        public FinalScanHostNative.HostStatus Status => _status;
        public bool Ready => _native && _status == FinalScanHostNative.HostStatus.Ready;
        public int InitResult => _initResult;
        public int SyntheticResult => _syntheticResult;
        public RenderMode Mode { get => renderMode; set => renderMode = value; }
        public string ModeSource { get; private set; } = "serialized";
        public Vector3 HeadPosition { get; private set; }
        public Quaternion HeadRotation { get; private set; } = Quaternion.identity;
        public Vector3 HeadVelocity { get; private set; }
        public double LastGpuFrameMs { get; private set; } = double.NaN;
        public int LastGpuHeadroomUs { get; private set; }
        public bool FrameTimingAvailable { get; private set; }
        public double PredictedDisplayTimeSec { get; private set; }
        public long VisibleSurfels => _cull[1];
        public long DrawRecords => _cull[2];
        public long CulledPages => _cull[0];
        public long CullGpuUs => _cull[3];
        public int ResidentPages { get; private set; }
        public int LogicalPages { get; private set; }
        public long FrontSurfels { get; private set; }
        public long BackSurfels { get; private set; }
        public uint FrontGeneration { get; private set; }
        public bool SpinTestRequestedByProp { get; private set; }
        public int SyntheticKind => syntheticKind;
        public bool SyntheticWorldEnabled => enableSyntheticWorld != 0 && syntheticKind >= 0;
        /// <summary>FS_CTR_MEASUREMENTS accepted per second (1 s window).</summary>
        public double MeasurementsPerSec { get; private set; }
        public long MeasurementsTotal { get; private set; }
        public long MeasurementsDropped { get; private set; }
        public EnvDepthFeeder EnvDepth => _envDepth;
        public HostLog Log => _log;

        /// <summary>Zone stats (innerPages, warmPages, prefetchPages, requestsThisFrame, orientationRequests, evictions, loads, stalls); refreshed every frame.</summary>
        public long[] ZoneStats => _zone;
        public long[] CullStats => _cull;
        public bool GetClassStats(FinalScanHostNative.JobClass cls, long[] out8) => _native && FinalScanHostNative.GetClassStats(cls, out8) == FinalScanHostNative.ResultOk;

        // ---- scan gate (C22 START / STOP SCAN) ----------------------------------------------------------------------
        static bool s_scanEnabled = true;
        /// <summary>Raised on the main thread when <see cref="ScanEnabled"/> changes (sensor authority / feeders subscribe).</summary>
        public static event Action<bool> ScanEnabledChanged;
        /// <summary>
        /// True while scan ticks are requested. Enforced in FinalScanHostNative.RequestScanTick and by EnvDepthFeeder
        /// (measurement priors pause while stopped); SensorAuthority follows it with its capture profile governor.
        /// Rendering, residency and publication never stop.
        /// </summary>
        public static bool ScanEnabled
        {
            get => s_scanEnabled;
            set
            {
                if (s_scanEnabled == value) return;
                s_scanEnabled = value;
                FinalScanHostNative.ScanRequestsEnabled = value;
                Debug.Log("[FinalScan] scan " + (value ? "START" : "STOP"));
                try { ScanEnabledChanged?.Invoke(value); } catch (Exception e) { Debug.LogException(e); }
            }
        }
        public bool Scanning { get => ScanEnabled; set => ScanEnabled = value; }
        public void ToggleScanning() => ScanEnabled = !ScanEnabled;
        public void CycleMode() { renderMode = (RenderMode)(((int)renderMode + 1) % 3); ModeSource = "ui"; _modeSent = false; }
        public string LastResetResult { get; private set; } = "none";

        /// <summary>
        /// RESET WORLD (C22 / C09R §25): the dedicated native world reset transaction (FsWorld_Reset). Observations stop,
        /// empty roots are published through the graphics path, allocations are freed after retirement, the page map and
        /// ids reset. The executor, its warmed pipelines, the sensor authority and the renderer stay alive.
        /// </summary>
        public bool ResetWorld()
        {
            if (!_native) { LastResetResult = "native absent"; Debug.Log("[FinalScan] reset skipped: " + LastResetResult); return false; }
            int rc = FinalScanHostNative.ResetWorld();
            _syntheticRequested = false;
            LastResetResult = $"FsWorld_Reset rc={rc}";
            Debug.Log("[FinalScan] RESET WORLD: " + LastResetResult);
            _log?.Emit(TelemetryPrefix, "{\"event\":\"reset\",\"rc\":" + rc + "}");
            return rc == FinalScanHostNative.ResultOk;
        }

        // ---- IRenderFrameHost ---------------------------------------------------------------------------------------
        public IntPtr RenderEventFunc => _renderEventFunc;

        public void OnFrameView(StereoView view, PrevDepthCapture prevDepth)
        {
            if (!_native) return;
            Array.Copy(view.HeadPos, _headPosView, 3);
            FinalScanHostNative.SetView(view.ViewL, view.ProjL, view.ViewR, view.ProjR, _headPosView);
            if (prevDepth != null && prevDepth.TryGetLatest(out RenderTexture rt, out StereoView prev) && prev.FrameIndex != _prevDepthFrame)
            {
                IntPtr ptr = rt.GetNativeTexturePtr();
                if (ptr != IntPtr.Zero)
                {
                    _prevDepthResult = FinalScanHostNative.SetPrevDepth(ptr, (uint)rt.width, (uint)rt.height, (uint)Mathf.Max(1, rt.volumeDepth), prev.ViewL, prev.ProjL, prev.ViewR, prev.ProjR);
                    _prevDepthFrame = prev.FrameIndex;
                }
            }
        }

        // ---- lifecycle ---------------------------------------------------------------------------------------------
        void Awake()
        {
            Current = this;
            _log = HostLog.Open(System.IO.Path.Combine(Application.persistentDataPath, "host"));
            try { WorldAbi.AssertLayout(); FinalScanHostNative.AssertLayout(); }
            catch (Exception e) { Debug.LogError("[FinalScan] " + e.Message); enabled = false; return; }

            _native = FinalScanHostNative.Available;
            if (!_native)
            {
                Debug.LogWarning($"[FinalScan] native host unavailable (abi={FinalScanHostNative.ReportedAbiVersion}); running without the executor.");
                return;
            }
            FinalScanHostNative.SetStorageRoot(Application.persistentDataPath);
            // Synthetic world is an acceptance fixture: only the system property enables it, never the inspector default.
            syntheticKind = AndroidSystemProps.ParseSyntheticKind(AndroidSystemProps.Get(AndroidSystemProps.SyntheticProp));
            enableSyntheticWorld = syntheticKind >= 0 ? 1u : 0u;
            if (enableSyntheticWorld != 0) Debug.LogWarning($"[FinalScan] synthetic world fixture requested by {AndroidSystemProps.SyntheticProp}: kind {syntheticKind} (not the live path)");
            var cfg = BuildConfig();
            _initResult = FinalScanHostNative.Init(ref cfg);
            _initCalled = true;
            _renderEventFunc = FinalScanHostNative.RenderEventFunc;
            Debug.Log($"[FinalScan] FsHost_Init rc={_initResult} abi={FinalScanHostNative.ReportedAbiVersion} storageRoot={Application.persistentDataPath} renderEventFunc={(_renderEventFunc != IntPtr.Zero)}");
        }

        void OnEnable()
        {
            FinalScanRenderFeature.Host = this;
            _sensor = GetComponent<SensorAuthority>();
            if (_sensor == null) Debug.LogError("[FinalScan] SensorAuthority missing on " + name + ": no PCA / Environment Depth ingest (the editor setup attaches it).");
            _envDepth ??= new EnvDepthFeeder(this, _sensor);
        }

        void OnDisable()
        {
            if (ReferenceEquals(FinalScanRenderFeature.Host, this)) FinalScanRenderFeature.Host = null;
        }

        void OnDestroy()
        {
            if (_native && _initCalled) { int rc = FinalScanHostNative.Shutdown(); Debug.Log("[FinalScan] FsHost_Shutdown rc=" + rc); }
            _log?.Dispose();
            if (Current == this) Current = null;
        }

        /// <summary>FsHostConfig from the serialized fields (pure; tested).</summary>
        public FinalScanHostNative.HostConfig BuildConfig()
        {
            var c = FinalScanHostNative.HostConfig.Create();
            c.residentPages = residentPages;
            c.canonicalSurfels = canonicalSurfels;
            c.indexLeaves = indexLeaves; c.indexNodes = indexNodes; c.renderBlocks = renderBlocks; c.renderNodes = renderNodes;
            c.pageHashCapacity = pageHashCapacity;
            c.measurementRingCapacity = measurementRingCapacity;
            c.drawRecordCapacity = drawRecordCapacity;
            c.frameBudgetUs = frameBudgetUs;
            for (int i = 0; i < FinalScanHostNative.JobClassCount; i++)
            {
                c.SetInFlightMax((FinalScanHostNative.JobClass)i, inFlightMax != null && i < inFlightMax.Length ? inFlightMax[i] : 1u);
                c.SetQuantumUs((FinalScanHostNative.JobClass)i, quantumUs != null && i < quantumUs.Length ? quantumUs[i] : 1000u);
            }
            c.useScannerQueue = useScannerQueue;
            c.enableSyntheticWorld = enableSyntheticWorld;
            return c;
        }

        // ---- per frame ---------------------------------------------------------------------------------------------
        void Update()
        {
            UpdateHeadPose();
            PollProps();
            if (!_native) { _envDepth?.Update(); return; }

            _status = FinalScanHostNative.Status;
            if (_status != _lastLoggedStatus)
            {
                Debug.Log($"[FinalScan] host status {_lastLoggedStatus} -> {_status}");
                _lastLoggedStatus = _status;
            }
            PollScheduledReset();
            if (_status == FinalScanHostNative.HostStatus.Ready)
            {
                if (enableSyntheticWorld != 0 && syntheticKind >= 0 && !_syntheticRequested)
                {
                    _syntheticRequested = true;
                    _syntheticResult = FinalScanHostNative.CreateSyntheticWorld((FinalScanHostNative.SyntheticKind)syntheticKind, syntheticExtentM, syntheticSurfelSpacingM, syntheticSeed);
                    Debug.Log($"[FinalScan] FsWorld_CreateSynthetic kind={syntheticKind} extent={syntheticExtentM} spacing={syntheticSurfelSpacingM} seed={syntheticSeed} rc={_syntheticResult}");
                }
                SendLodPolicy();
                SendMode();
            }

            SendFrameHint();
            SendGpuHeadroom();
            _envDepth?.Update();
            RefreshStats();
            EmitTelemetry();
        }

        void UpdateHeadPose()
        {
            Camera cam = Camera.main;
            Transform t = cam != null ? cam.transform : transform;
            Vector3 pos = t.position;
            float dt = Time.unscaledDeltaTime;
            HeadVelocity = _havePrevHead ? Residency.ResidencyPrediction.UpdateVelocity(HeadVelocity, _prevHeadPos, pos, dt) : Vector3.zero;
            _prevHeadPos = pos; _havePrevHead = true;
            HeadPosition = pos;
            HeadRotation = t.rotation;
        }

        void SendFrameHint()
        {
            double now = -1, hz = 72;
            try
            {
                if (OVRPlugin.initialized)
                {
                    now = OVRPlugin.GetTimeInSeconds();
                    float f = OVRPlugin.systemDisplayFrequency;
                    if (f > 1f) hz = f;
                }
            }
            catch (Exception) { now = -1; }
            if (now < 0) now = Time.realtimeSinceStartupAsDouble;
            PredictedDisplayTimeSec = HostMath.PredictedDisplayTime(now, (float)hz, extraDisplayLatencyMs * 0.001);
            HostMath.ToArray(HeadPosition, _headPos);
            HostMath.ToArray(HeadVelocity, _headVel);
            HostMath.ToArray(HeadRotation, _headRot);
            FinalScanHostNative.SetFrameHint(PredictedDisplayTimeSec, _headPos, _headVel, _headRot);
        }

        void SendGpuHeadroom()
        {
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _frameTiming) > 0)
                {
                    FrameTimingAvailable = true;
                    LastGpuFrameMs = _frameTiming[0].gpuFrameTime;
                }
                else FrameTimingAvailable = false;
            }
            catch (Exception) { FrameTimingAvailable = false; }
            LastGpuHeadroomUs = HostMath.GpuHeadroomUs(FrameTimingAvailable ? LastGpuFrameMs : double.NaN);
            FinalScanHostNative.SetGpuHeadroomUs(LastGpuHeadroomUs);
        }

        void SendLodPolicy()
        {
            uint budget = SurfelRenderer.Current != null ? SurfelRenderer.Current.ScreenWorkBudget : 200000u;
            if (_lodPolicySent && budget == _lodBudgetSent) return;
            int rc = FinalScanHostNative.SetLodPolicy(fovealErrorPx, peripheralErrorPx, predictionMarginDeg, budget);
            _lodPolicySent = rc == FinalScanHostNative.ResultOk;
            _lodBudgetSent = budget;
            Debug.Log($"[FinalScan] FsRender_SetLodPolicy foveal={fovealErrorPx} peripheral={peripheralErrorPx} margin={predictionMarginDeg} budget={budget} rc={rc}");
        }

        void SendMode()
        {
            if (SurfelRenderer.Current != null) SurfelRenderer.Current.Mode = renderMode;
            if (_modeSent && _modeSentValue == renderMode) return;
            int rc = FinalScanHostNative.SetMode((FinalScanHostNative.RenderModeId)(int)renderMode);
            if (rc == FinalScanHostNative.ResultOk) { _modeSent = true; _modeSentValue = renderMode; Debug.Log($"[FinalScan] FsRender_SetMode {renderMode} ({ModeSource})"); }
            else Debug.LogWarning($"[FinalScan] FsRender_SetMode {renderMode} rc={rc}");
        }

        void PollProps()
        {
            if (Time.unscaledTime < _nextPropPoll) return;
            _nextPropPoll = Time.unscaledTime + Mathf.Max(0.5f, propPollIntervalSec);
            if (RenderModeParser.TryParse(AndroidSystemProps.Get(AndroidSystemProps.ModeProp), out RenderMode m) && m != renderMode)
            {
                renderMode = m; ModeSource = "setprop";
                _modeSent = false;
            }
            SpinTestRequestedByProp = AndroidSystemProps.IsTruthy(AndroidSystemProps.Get(AndroidSystemProps.SpinTestProp));
            if (_resetAtSec < 0f && float.TryParse(AndroidSystemProps.Get(AndroidSystemProps.ResetAtProp), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float at) && at > 0f)
            {
                _resetAtSec = at; Debug.Log($"[FinalScan] acceptance RESET WORLD scheduled {at:0.#} s after READY ({AndroidSystemProps.ResetAtProp})");
            }
        }
        float _resetAtSec = -1f, _readyAt = -1f; bool _resetFired;
        void PollScheduledReset()
        {
            if (_status != FinalScanHostNative.HostStatus.Ready) return;
            if (_readyAt < 0f) _readyAt = Time.unscaledTime;
            if (_resetFired || _resetAtSec <= 0f || Time.unscaledTime - _readyAt < _resetAtSec) return;
            _resetFired = true;
            ResetWorld();
        }

        void RefreshStats()
        {
            FinalScanHostNative.GetZoneStats(_zone);
            FinalScanHostNative.GetLastCullStats(_cull);
            if (FinalScanHostNative.GetPageCount(out int resident, out int logical) == FinalScanHostNative.ResultOk) { ResidentPages = resident; LogicalPages = logical; }
            if (FinalScanHostNative.GetSurfelCount(out long front, out long back) == FinalScanHostNative.ResultOk) { FrontSurfels = front; BackSurfels = back; }
            FrontGeneration = FinalScanHostNative.FrontGeneration;
            MeasurementsTotal = FinalScanHostNative.GetCounter(FsCounter.Measurements);
            MeasurementsDropped = FinalScanHostNative.GetCounter(FsCounter.MeasurementsDropped);
            double now = Time.realtimeSinceStartupAsDouble;
            if (_measWindowStart < 0) { _measWindowStart = now; _measWindowBase = MeasurementsTotal; }
            else if (now - _measWindowStart >= 1.0)
            {
                MeasurementsPerSec = (MeasurementsTotal - _measWindowBase) / (now - _measWindowStart);
                _measWindowStart = now; _measWindowBase = MeasurementsTotal;
            }
        }
        double _measWindowStart = -1; long _measWindowBase;

        void EmitTelemetry()
        {
            if (Time.unscaledTime < _nextTelemetry) return;
            _nextTelemetry = Time.unscaledTime + Mathf.Max(0.5f, telemetryIntervalSec);
            string native = "{}", world = "{}", render = "{}";
            try { native = FinalScanHostNative.TelemetryJson(); } catch (Exception e) { native = "{\"error\":" + JsonWriter.Escape(e.Message) + "}"; }
            try { world = FinalScanHostNative.WorldTelemetryJson(); } catch (Exception e) { world = "{\"error\":" + JsonWriter.Escape(e.Message) + "}"; }
            try { render = FinalScanHostNative.RenderTelemetryJson(); } catch (Exception e) { render = "{\"error\":" + JsonWriter.Escape(e.Message) + "}"; }
            var w = new JsonWriter(1024);
            w.BeginObject()
             .Prop("t", Time.realtimeSinceStartupAsDouble)
             .Prop("frame", Time.frameCount)
             .Prop("status", _status.ToString())
             .Prop("mode", RenderModeParser.Name(renderMode))
             .Prop("frameMs", Time.unscaledDeltaTime * 1000.0)
             .Prop("gpuMs", LastGpuFrameMs)
             .Prop("gpuHeadroomUs", LastGpuHeadroomUs)
             .Prop("frameTiming", FrameTimingAvailable)
             .Prop("residentPages", ResidentPages).Prop("logicalPages", LogicalPages)
             .Prop("frontSurfels", FrontSurfels).Prop("backSurfels", BackSurfels).Prop("frontGeneration", (long)FrontGeneration)
             .Prop("measurementsPerSec", MeasurementsPerSec).Prop("measurements", MeasurementsTotal).Prop("measurementsDropped", MeasurementsDropped)
             .Prop("synthetic", SyntheticWorldEnabled).Prop("syntheticKind", syntheticKind)
             .Prop("scanEnabled", ScanEnabled).Prop("scanTicksGated", FinalScanHostNative.ScanTicksGated)
             .Prop("visibleSurfels", VisibleSurfels).Prop("drawRecords", DrawRecords).Prop("cullGpuUs", CullGpuUs)
             .Prop("orientationRequests", _zone[4]).Prop("requestsThisFrame", _zone[3])
             .Prop("buffersRegistered", SurfelRenderer.Current != null && SurfelRenderer.Current.Registered)
             .Prop("prevDepthRc", _prevDepthResult)
             .Prop("envDepthAccepted", _envDepth != null ? _envDepth.Accepted : 0)
             .Prop("envDepthRc", _envDepth != null ? _envDepth.LastResult : -1).Prop("measEnvDepthRc", _envDepth != null ? _envDepth.LastMeasResult : -1)
             .Prop("envDepthSource", _envDepth != null ? _envDepth.Source : "none")
             .Prop("syntheticRc", _syntheticResult)
             .PropRaw("native", native)
             .PropRaw("world", world)
             .PropRaw("render", render)
             .EndObject();
            _log.Emit(TelemetryPrefix, w.ToString());
            _telemetryLines++;
        }
    }
}
