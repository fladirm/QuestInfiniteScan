using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FinalScan.Platform.Native;
using FinalScan.Render;
using Unity.XR.CoreUtils.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace FinalScan.Host
{
    /// <summary>
    /// Feeds the Environment Depth occlusion prior (contract §13.5, §6) to FsRender_SetEnvDepth.
    ///
    /// Source selection: when <c>FinalScan.Platform.Sensor.SensorAuthority</c> (C03, another agent) exists in the
    /// scene it is the single owner of the depth subscription (donor lesson: double Env Depth acquire) and exposes
    /// <c>LatestDepth</c>; this class reads it by reflection to stay decoupled. Otherwise it subscribes to
    /// AROcclusionManager.frameReceived itself. Frames are deduplicated by XrTime timestamp (25 Hz sensor behind a
    /// 72 Hz callback, C01 evidence).
    ///
    /// ABI mapping: poses → 4x4 world-from-eye TRS matrices (column-major); fovs → [tan left, tan right, tan up,
    /// tan down] with provider signs; near/far as reported (far may be 0/inf = unbounded); texture = the 320x320x2
    /// Tex2DArray external RenderTexture's native pointer.
    /// </summary>
    public sealed class EnvDepthFeeder
    {
        public const string SensorAuthorityTypeName = "FinalScan.Platform.Sensor.SensorAuthority";

        readonly FinalScanHost _host;
        readonly DepthFrameDedupe _dedupe = new DepthFrameDedupe();
        readonly float[] _poseL = new float[16], _poseR = new float[16], _fovL = new float[4], _fovR = new float[4];
        AROcclusionManager _occ;
        bool _subscribed, _addedManager;
        UnityEngine.Object _authority;
        MemberInfo _latestMember;
        int _lastRc, _lastMeasRc;
        int _errors;
        string _source = "none";

        public EnvDepthFeeder(FinalScanHost host) { _host = host; }

        public string Source => _source;
        public int Accepted => _dedupe.Accepted;
        public int Repeats => _dedupe.Repeats;
        public int LastResult => _lastRc;
        public int LastMeasResult => _lastMeasRc;
        public long Pushed { get; private set; }
        public long LastTimestampNs => _dedupe.LastTimestampNs;

        public void Start()
        {
            Type authorityType = FindType(SensorAuthorityTypeName);
            if (authorityType != null)
            {
                _authority = UnityEngine.Object.FindAnyObjectByType(authorityType, FindObjectsInactive.Include);
                _latestMember = (MemberInfo)authorityType.GetProperty("LatestDepth", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? authorityType.GetField("LatestDepth", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_authority != null && _latestMember != null) { _source = "SensorAuthority"; Debug.Log("[FinalScan] Env Depth via SensorAuthority.LatestDepth (reflection)"); return; }
                Debug.LogWarning("[FinalScan] SensorAuthority type present but no instance/LatestDepth; falling back to AROcclusionManager.");
            }
            SubscribeOcclusion();
        }

        public void Stop()
        {
            if (_subscribed && _occ != null) { try { _occ.frameReceived -= OnFrame; } catch (Exception) { } }
            _subscribed = false;
        }

        void SubscribeOcclusion()
        {
            try
            {
                _occ = UnityEngine.Object.FindAnyObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
                if (_occ == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null) { _occ = cam.gameObject.AddComponent<AROcclusionManager>(); _addedManager = true; }
                }
                if (_occ == null) { _source = "none"; return; }
                _occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
                _occ.enabled = true;
                _occ.frameReceived += OnFrame;
                _subscribed = true;
                _source = "AROcclusionManager";
                Debug.Log($"[FinalScan] Env Depth via AROcclusionManager (added={_addedManager})");
            }
            catch (Exception e) { Debug.LogWarning("[FinalScan] Env Depth subscription failed: " + e.Message); _source = "none"; }
        }

        /// <summary>Polls the SensorAuthority path (event path pushes on its own).</summary>
        public void Update()
        {
            if (_authority == null || _latestMember == null) return;
            try
            {
                object latest = _latestMember is PropertyInfo p ? p.GetValue(_authority) : ((FieldInfo)_latestMember).GetValue(_authority);
                if (latest == null) return;
                var r = new Reflected(latest);
                long ts = r.Long("xrTimeNs", "XrTimeNs", "timestampNs", "TimestampNs");
                if (!_dedupe.Accept(ts != long.MinValue, ts)) return;
                Texture tex = r.Get<Texture>("texture", "Texture");
                if (tex == null) return;
                if (!r.TryPoses(out Pose pl, out Pose pr)) return;
                if (!r.TryFovs(_fovL, _fovR)) return;
                float near = (float)r.Double("near", "Near", "nearZ", "NearZ", 0.1);
                float far = (float)r.Double("far", "Far", "farZ", "FarZ", 0.0);
                Push(tex, pl, pr, near, far, ts);
            }
            catch (Exception e) { if (_errors++ < 3) Debug.LogWarning("[FinalScan] SensorAuthority.LatestDepth read failed: " + e.Message); }
        }

        void OnFrame(AROcclusionFrameEventArgs a)
        {
            try
            {
                bool hasTs = a.TryGetTimestamp(out long ts);
                if (!_dedupe.Accept(hasTs, ts)) return;
                ReadOnlyList<ARExternalTexture> ext = a.externalTextures;
                if (ext == null || ext.Count == 0) return;
                Texture tex = ext[0].texture;
                if (tex == null) return;
                if (!a.TryGetPoses(out ReadOnlyList<Pose> poses) || poses.Count == 0) return;
                if (!a.TryGetFovs(out ReadOnlyList<XRFov> fovs) || fovs.Count == 0) return;
                Pose pl = poses[0], pr = poses.Count > 1 ? poses[1] : poses[0];
                XRFov fl = fovs[0], fr = fovs.Count > 1 ? fovs[1] : fovs[0];
                HostMath.FovTangents(fl.angleLeft, fl.angleRight, fl.angleUp, fl.angleDown, _fovL);
                HostMath.FovTangents(fr.angleLeft, fr.angleRight, fr.angleUp, fr.angleDown, _fovR);
                float near = 0.1f, far = 0f;
                if (a.TryGetNearFarPlanes(out XRNearFarPlanes planes)) { near = planes.nearZ; far = planes.farZ; }
                Push(tex, pl, pr, near, far, ts);
            }
            catch (Exception e) { if (_errors++ < 3) Debug.LogWarning("[FinalScan] Env Depth frame failed: " + e.Message); }
        }

        public long PausedFrames { get; private set; }

        void Push(Texture tex, Pose pl, Pose pr, float near, float far, long xrTimeNs)
        {
            if (!_host.NativeAvailable) return;
            if (!FinalScanHost.ScanEnabled) { PausedFrames++; return; }   // STOP SCAN: no new measurement priors
            IntPtr ptr = tex.GetNativeTexturePtr();
            if (ptr == IntPtr.Zero) return;
            HostMath.ToColumnMajor(Matrix4x4.TRS(pl.position, pl.rotation, Vector3.one), _poseL);
            HostMath.ToColumnMajor(Matrix4x4.TRS(pr.position, pr.rotation, Vector3.one), _poseR);
            if (float.IsInfinity(far) || float.IsNaN(far)) far = 0f;
            _lastRc = FinalScanHostNative.SetEnvDepth(ptr, (uint)tex.width, (uint)tex.height, _poseL, _poseR, _fovL, _fovR, near, far, xrTimeNs);
            // C09 measurement module twin (same frame, same handle) when the plugin exports it.
            _lastMeasRc = FinalScanHostNative.SetMeasEnvDepth(ptr, (uint)tex.width, (uint)tex.height, _poseL, _poseR, _fovL, _fovR, near, far, xrTimeNs);
            Pushed++;
        }

        static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch (Exception) { }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Tolerant reader for the reflected LatestDepth object (fields or properties, several spellings).</summary>
        sealed class Reflected
        {
            readonly object _o; readonly Type _t;
            public Reflected(object o) { _o = o; _t = o.GetType(); }
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            public object Raw(params string[] names)
            {
                foreach (string n in names)
                {
                    PropertyInfo p = _t.GetProperty(n, F); if (p != null) return p.GetValue(_o);
                    FieldInfo f = _t.GetField(n, F); if (f != null) return f.GetValue(_o);
                }
                return null;
            }
            public T Get<T>(params string[] names) where T : class => Raw(names) as T;
            public long Long(params string[] names) { object v = Raw(names); return v is long l ? l : v is int i ? i : v is ulong u ? (long)u : long.MinValue; }
            public double Double(string a, string b, string c, string d, double fallback) { object v = Raw(a, b, c, d); return v is float f ? f : v is double x ? x : fallback; }

            public bool TryPoses(out Pose l, out Pose r)
            {
                l = r = Pose.identity;
                object v = Raw("poses", "Poses");
                if (v is Pose[] arr && arr.Length > 0) { l = arr[0]; r = arr.Length > 1 ? arr[1] : arr[0]; return true; }
                if (v is IList<Pose> list && list.Count > 0) { l = list[0]; r = list.Count > 1 ? list[1] : list[0]; return true; }
                if (v is IEnumerable e)
                {
                    var ps = e.OfType<Pose>().ToList();
                    if (ps.Count > 0) { l = ps[0]; r = ps.Count > 1 ? ps[1] : ps[0]; return true; }
                    var ms = e.OfType<Matrix4x4>().ToList();
                    if (ms.Count > 0) { l = FromMatrix(ms[0]); r = FromMatrix(ms.Count > 1 ? ms[1] : ms[0]); return true; }
                }
                return false;
            }

            static Pose FromMatrix(Matrix4x4 m) => new Pose(m.GetColumn(3), m.rotation);

            /// <summary>fovs as XRFov list (radians) or as tangents: Vector4[] (SensorAuthority DepthFrame.fovTangents) / float[8].</summary>
            public bool TryFovs(float[] l, float[] r)
            {
                object v = Raw("fovTangents", "FovTangents", "fovs", "Fovs");
                if (v is float[] f8 && f8.Length >= 8) { Array.Copy(f8, 0, l, 0, 4); Array.Copy(f8, 4, r, 0, 4); return true; }
                if (v is Vector4[] v4 && v4.Length > 0) { Fill(v4[0], l); Fill(v4.Length > 1 ? v4[1] : v4[0], r); return true; }
                if (v is IEnumerable e)
                {
                    var fs = e.OfType<XRFov>().ToList();
                    if (fs.Count > 0)
                    {
                        XRFov a = fs[0], b = fs.Count > 1 ? fs[1] : fs[0];
                        HostMath.FovTangents(a.angleLeft, a.angleRight, a.angleUp, a.angleDown, l);
                        HostMath.FovTangents(b.angleLeft, b.angleRight, b.angleUp, b.angleDown, r);
                        return true;
                    }
                    var vs = e.OfType<Vector4>().ToList();
                    if (vs.Count > 0) { Fill(vs[0], l); Fill(vs.Count > 1 ? vs[1] : vs[0], r); return true; }
                }
                return false;
            }

            static void Fill(Vector4 v, float[] dst) { dst[0] = v.x; dst[1] = v.y; dst[2] = v.z; dst[3] = v.w; }
        }
    }
}
