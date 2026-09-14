using System;
using FinalScan.Host;
using FinalScan.Platform.Native;
using FinalScan.Telemetry;
using UnityEngine;

namespace FinalScan.Residency
{
    /// <summary>
    /// C07 residency driver (contract §12): every frame the 360° readout sphere centre is
    /// <c>predicted = head + clamp(velocity * 0.5 s, 1 m)</c> with INNER 4 m / WARM 8 m / PREFETCH 12 m radii, sent
    /// with FsResidency_SetCenter. Position and linear velocity are the ONLY inputs (ResidencyPrediction has no
    /// orientation parameter by construction); head yaw/pitch never reach this class.
    ///
    /// Spin test (§21.2/§21.8): enabled by the serialized flag or <c>setprop debug.finalscan.spintest 1</c>; logs one
    /// "FS-SPIN {...}" line per frame for 30 s (yaw rate, zone stats, visible surfels, frame ms) and finishes with
    /// "FS-ACCEPT spin {..., verdict: PASS|FAIL}" from <see cref="SpinAcceptance"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class ResidencyDriver : MonoBehaviour
    {
        public static ResidencyDriver Current { get; private set; }

        [SerializeField] float innerRadiusM = 4f;
        [SerializeField] float warmRadiusM = 8f;
        [SerializeField] float prefetchRadiusM = 12f;
        [SerializeField] float predictionHorizonSec = ResidencyPrediction.DefaultHorizonSec;
        [SerializeField] float maxLeadM = ResidencyPrediction.DefaultMaxLeadM;
        [Header("Spin test")]
        [SerializeField] bool spinTest;
        [SerializeField] float spinDurationSec = 30f;
        [Tooltip("Seconds after the world reports visible surfels before the spin window starts counting (lets residency warm up).")]
        [SerializeField] float spinWarmupSec = 3f;

        Vector3 _prevPos;
        Quaternion _prevRot = Quaternion.identity;
        bool _havePrev;
        Vector3 _velocity;
        readonly float[] _center = new float[3];
        int _lastRc;
        long _centerUpdates;

        SpinAcceptance _spin;
        bool _spinActive, _spinDone;
        double _spinStart, _spinWarmStart = -1;
        int _spinFrame;
        string _spinTrigger;

        public Vector3 PredictedCenter { get; private set; }
        public Vector3 Velocity => _velocity;
        public int LastResult => _lastRc;
        public long CenterUpdates => _centerUpdates;
        public bool SpinActive => _spinActive;
        public double SpinElapsedSec => _spinActive ? Time.realtimeSinceStartupAsDouble - _spinStart : 0;
        public float LastYawRateDegPerSec { get; private set; }
        public string LastVerdict { get; private set; }
        public SpinAcceptance.Result LastResultDetail { get; private set; }

        void OnEnable() { Current = this; }
        void OnDisable() { if (Current == this) Current = null; }

        void Update()
        {
            FinalScanHost host = FinalScanHost.Current;
            Camera cam = Camera.main;
            Transform t = cam != null ? cam.transform : transform;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            float dt = Time.unscaledDeltaTime;

            if (_havePrev)
            {
                _velocity = ResidencyPrediction.UpdateVelocity(_velocity, _prevPos, pos, dt);
                LastYawRateDegPerSec = ResidencyPrediction.YawRateDegPerSec(_prevRot, rot, dt);   // telemetry only, never an input below
            }
            _prevPos = pos; _prevRot = rot; _havePrev = true;

            PredictedCenter = ResidencyPrediction.PredictCenter(pos, _velocity, predictionHorizonSec, maxLeadM);
            if (host != null && host.NativeAvailable)
            {
                _center[0] = PredictedCenter.x; _center[1] = PredictedCenter.y; _center[2] = PredictedCenter.z;
                _lastRc = FinalScanHostNative.SetResidencyCenter(_center, innerRadiusM, warmRadiusM, prefetchRadiusM);
                _centerUpdates++;
            }

            UpdateSpinTest(host);
        }

        void UpdateSpinTest(FinalScanHost host)
        {
            if (_spinDone || host == null) return;
            bool wanted = spinTest || host.SpinTestRequestedByProp;
            if (!_spinActive)
            {
                if (!wanted) return;
                // Warm-up: wait until the world is visible for spinWarmupSec so residency has something to serve.
                bool warm = host.VisibleSurfels > 0 || !host.NativeAvailable;
                double now = Time.realtimeSinceStartupAsDouble;
                if (!warm) { _spinWarmStart = -1; return; }
                if (_spinWarmStart < 0) _spinWarmStart = now;
                if (now - _spinWarmStart < spinWarmupSec) return;
                _spin = new SpinAcceptance();
                _spinActive = true; _spinStart = now; _spinFrame = 0;
                _spinTrigger = spinTest ? "serialized" : "setprop";
                host.Log?.Emit(HostLog.SpinPrefix, "{\"event\":\"start\",\"trigger\":\"" + _spinTrigger + "\",\"durationSec\":" + spinDurationSec.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                return;
            }

            long[] z = host.ZoneStats;
            var s = new SpinAcceptance.Sample
            {
                TimeSec = Time.realtimeSinceStartupAsDouble,
                YawRateDegPerSec = LastYawRateDegPerSec,
                RequestsThisFrame = z[3],
                OrientationRequests = z[4],
                VisibleSurfels = host.VisibleSurfels,
                FrameMs = Time.unscaledDeltaTime * 1000.0,
                FacingGeometry = FacingGeometry(host),
            };
            _spin.Add(s);
            _spinFrame++;
            var w = new JsonWriter(256);
            w.BeginObject().Prop("frame", _spinFrame).Prop("t", s.TimeSec - _spinStart).Prop("yawRate", s.YawRateDegPerSec)
             .Prop("requestsThisFrame", s.RequestsThisFrame).Prop("orientationRequests", s.OrientationRequests)
             .Prop("visible", s.VisibleSurfels).Prop("frameMs", s.FrameMs).Prop("gpuMs", host.LastGpuFrameMs)
             .Prop("inner", z[0]).Prop("warm", z[1]).Prop("prefetch", z[2]).Prop("loads", z[6]).Prop("stalls", z[7])
             .Prop("facing", s.FacingGeometry).EndObject();
            host.Log?.Emit(HostLog.SpinPrefix, w.ToString());

            if (s.TimeSec - _spinStart >= spinDurationSec)
            {
                SpinAcceptance.Result r = _spin.Evaluate();
                LastResultDetail = r;
                LastVerdict = r.Pass ? "PASS" : "FAIL: " + string.Join("; ", r.Reasons);
                host.Log?.Emit(HostLog.AcceptPrefix, "spin " + r.ToJson());
                _spinActive = false; _spinDone = true;
            }
        }

        /// <summary>
        /// "Facing geometry" for the zero-visible check: closed synthetic scenes (room box, corridor, stairs) surround
        /// the user, so every frame faces geometry once the world is published; for open scenes (thin wall, dense) a
        /// frame counts only when the world has been visible within the last 0.5 s.
        /// </summary>
        double _lastVisibleTime = -1;
        bool FacingGeometry(FinalScanHost host)
        {
            if (!host.NativeAvailable) return false;
            bool published = host.FrontSurfels > 0 && host.ResidentPages > 0;
            if (host.VisibleSurfels > 0) _lastVisibleTime = Time.realtimeSinceStartupAsDouble;
            if (!published) return false;
            if (host.SyntheticWorldEnabled && host.SyntheticKind <= (int)FinalScanHostNative.SyntheticKind.Stairs) return true;
            return _lastVisibleTime >= 0 && Time.realtimeSinceStartupAsDouble - _lastVisibleTime < 0.5;
        }

        /// <summary>Starts the spin test from code (PlayMode test).</summary>
        public void RequestSpinTest(float durationSec = 30f) { spinTest = true; spinDurationSec = durationSec; _spinDone = false; }
    }
}
