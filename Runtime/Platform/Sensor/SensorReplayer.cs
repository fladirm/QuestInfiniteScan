using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FinalScan.Telemetry;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// Deterministic replay of a <see cref="SensorRecorder"/> directory (contract §4.7). The index events are fed to a
    /// <see cref="SensorPipeline"/> in recorded order with the recorded raw values (clock references, pose samples, producer
    /// timestamps, runtime-located poses), so the clock gate, pairing and motion gate reproduce the device run bit for bit
    /// on the host (tests) and on the device (acceptance). Recorded observations are compared against the replayed ones
    /// (<see cref="ObservationMismatches"/>). Textures are optional: when <see cref="loadImages"/> is set and raw files exist,
    /// frames get Texture2D copies and depth frames Texture2DArray copies; otherwise metadata-only.
    /// </summary>
    public sealed class SensorReplayer
    {
        public readonly string Directory;
        public bool loadImages;
        readonly List<Dictionary<string, object>> _events = new List<Dictionary<string, object>>();
        int _cursor;
        SensorPipeline _pipeline; Action<string> _log;
        readonly Dictionary<long, Dictionary<string, object>> _recordedObs = new Dictionary<long, Dictionary<string, object>>();
        readonly CameraIntrinsicsData[] _intr = new CameraIntrinsicsData[2];
        readonly Dictionary<CameraEye, Texture2D[]> _imagePool = new Dictionary<CameraEye, Texture2D[]>();
        public DepthFrame LatestDepth { get; private set; }
        public int EventCount => _events.Count;
        public int Cursor => _cursor;
        public bool Finished => _cursor >= _events.Count;
        public long FramesReplayed { get; private set; }
        public long FramesRejected { get; private set; }
        public long ObservationsReplayed { get; private set; }
        public long ObservationsRecorded { get; private set; }
        public long ObservationMismatches { get; private set; }
        public long DepthReplayed { get; private set; }
        public long ImagesLoaded { get; private set; }
        public int LastRecordedFrame { get; private set; } = -1;

        public SensorReplayer(string directory) { Directory = directory; }

        /// <summary>Loads index.jsonl (or a single jsonl path).</summary>
        public static SensorReplayer Open(string directoryOrIndex)
        {
            string index = File.Exists(directoryOrIndex) ? directoryOrIndex : Path.Combine(directoryOrIndex, "index.jsonl");
            var r = new SensorReplayer(Path.GetDirectoryName(index));
            using (var sr = new StreamReader(index, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim(); if (line.Length == 0) continue;
                    if (MiniJson.Parse(line) is Dictionary<string, object> d) r._events.Add(d);
                }
            }
            return r;
        }

        /// <summary>Loads events from memory (tests).</summary>
        public static SensorReplayer FromLines(IEnumerable<string> lines)
        {
            var r = new SensorReplayer("<memory>");
            foreach (var line in lines) if (MiniJson.Parse(line) is Dictionary<string, object> d) r._events.Add(d);
            return r;
        }

        public void Attach(SensorPipeline pipeline, Action<string> log)
        {
            _pipeline = pipeline; _log = log; _cursor = 0;
            _pipeline.ObservationCreated -= OnReplayedObservation;
            _pipeline.ObservationCreated += OnReplayedObservation;
            _recordedObs.Clear();
            foreach (var e in _events) if (Str(e, "e") == "obs") { _recordedObs[Long(e, "id")] = e; ObservationsRecorded++; }
        }

        /// <summary>Replays the next recorded host frame (all events sharing the next "f"). Returns false when finished.</summary>
        public bool Step(int hostFrameCount)
        {
            if (_pipeline == null || Finished) return false;
            long f = Long(_events[_cursor], "f", -1);
            while (_cursor < _events.Count && Long(_events[_cursor], "f", -1) == f) Apply(_events[_cursor++]);
            LastRecordedFrame = (int)f;
            return !Finished;
        }

        public void RunAll() { while (Step(0)) { } }

        void Apply(Dictionary<string, object> e)
        {
            switch (Str(e, "e"))
            {
                case "clock": _pipeline.AddClockReference(Dbl(e, "ob"), Long(e, "utc"), Dbl(e, "oa"), Long(e, "m")); break;
                case "pose":
                    _pipeline.PushPose(new PoseSample { xrTimeNs = Long(e, "t"), head = PoseOf(e, "h"), eyeLeft = PoseOf(e, "l"), eyeRight = PoseOf(e, "r"), angularVelocity = Vec(e, "av"), linearVelocity = Vec(e, "lv"), valid = true });
                    break;
                case "intr":
                    {
                        int eye = (int)Long(e, "eye");
                        if (eye >= 0 && eye < 2) _intr[eye] = CameraIntrinsicsData.Create((float)Dbl(e, "fx"), (float)Dbl(e, "fy"), (float)Dbl(e, "cx"), (float)Dbl(e, "cy"), (int)Long(e, "w"), (int)Long(e, "h"), (int)Long(e, "sw"), (int)Long(e, "sh"), PoseOf(e, "l"));
                        break;
                    }
                case "profile":
                    if (Enum.TryParse(Str(e, "p"), out CaptureProfile p) && p != _pipeline.Profile) _pipeline.OnProfileApplied();
                    break;
                case "frame": ReplayFrame(e); break;
                case "depth": ReplayDepth(e); break;
                default: break; // obs, telemetry, meta: derived data
            }
        }

        void ReplayFrame(Dictionary<string, object> e)
        {
            var eye = (CameraEye)Long(e, "eye");
            bool rt = Bool(e, "rt"); Pose head = PoseOf(e, "h");
            RuntimeLocate locate = rt ? (long t, out Pose h) => { h = head; return true; } : (RuntimeLocate)null;
            if (!_pipeline.TryBeginFrame(eye, Long(e, "mono"), Long(e, "utc"), Dbl(e, "on"), Long(e, "mn"), _intr[(int)eye], locate, out var lease)) { FramesRejected++; return; }
            string img = Str(e, "img");
            if (loadImages && !string.IsNullOrEmpty(img)) LoadImage(lease, img, (int)Long(e, "w"), (int)Long(e, "h"));
            if (_pipeline.CommitFrame(lease)) FramesReplayed++; else FramesRejected++;
        }

        void LoadImage(CameraFrameLease lease, string rel, int w, int h)
        {
            try
            {
                string path = Path.Combine(Directory, rel);
                if (!File.Exists(path) || w <= 0 || h <= 0) return;
                if (!_imagePool.TryGetValue(lease.eye, out var pool)) { pool = new Texture2D[SensorPipeline.PoolPerEye]; _imagePool[lease.eye] = pool; }
                var tex = pool[lease.poolSlot];
                if (tex == null || tex.width != w || tex.height != h) { tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "FS replay " + lease.eye + " " + lease.poolSlot }; pool[lease.poolSlot] = tex; _pipeline.Pool(lease.eye).SetTexture(lease.poolSlot, tex); }
                tex.LoadRawTextureData(File.ReadAllBytes(path)); tex.Apply(false, false);
                lease.texture = tex; ImagesLoaded++;
            }
            catch (Exception ex) { _log?.Invoke("FS-SENSOR replay image failed: " + ex.Message); }
        }

        void ReplayDepth(Dictionary<string, object> e)
        {
            var d = new DepthFrame { frameId = Long(e, "id"), xrTimeNs = Long(e, "t"), receiveXrTimeNs = Long(e, "rn"), ageNs = Long(e, "age"), near = (float)Dbl(e, "near"), far = (float)Dbl(e, "far"), width = (int)Long(e, "w"), height = (int)Long(e, "h"), posesValid = Bool(e, "pv"), fovsValid = Bool(e, "fv"), planesValid = true };
            for (int i = 0; i < 2; i++)
            {
                d.poses[i] = PoseOf(e, "e" + i);
                d.poseMatrices[i] = SensorMath.Trs(d.poses[i]);
                var f = Arr(e, "fov" + i); if (f != null && f.Count >= 4) d.fovTangents[i] = new Vector4((float)ToD(f[0]), (float)ToD(f[1]), (float)ToD(f[2]), (float)ToD(f[3]));
            }
            string raw = Str(e, "raw");
            if (loadImages && !string.IsNullOrEmpty(raw))
            {
                try
                {
                    string path = Path.Combine(Directory, raw);
                    if (File.Exists(path) && d.width > 0 && d.height > 0)
                    {
                        var bytes = File.ReadAllBytes(path);
                        int slices = Math.Max(1, bytes.Length / (d.width * d.height * 4));
                        var arr = new Texture2DArray(d.width, d.height, slices, TextureFormat.RFloat, false) { name = "FS replay depth " + d.frameId };
                        for (int s = 0; s < slices; s++) { var slice = new byte[d.width * d.height * 4]; Buffer.BlockCopy(bytes, s * slice.Length, slice, 0, slice.Length); arr.SetPixelData(slice, 0, s); }
                        arr.Apply(false, false);
                        d.texture = arr;
                    }
                }
                catch (Exception ex) { _log?.Invoke("FS-SENSOR replay depth failed: " + ex.Message); }
            }
            d.Begin(df => { if (df.texture is Texture2DArray t) UnityEngine.Object.Destroy(t); df.texture = null; });
            var prev = LatestDepth; LatestDepth = d; prev?.Release();
            DepthReplayed++;
        }

        void OnReplayedObservation(StereoObservation o)
        {
            ObservationsReplayed++;
            if (_recordedObs.Count == 0) return; // metadata-only recording: nothing to compare against
            if (!_recordedObs.TryGetValue(o.observationId, out var rec)) { ObservationMismatches++; return; }
            if (Long(rec, "l") != o.frameIdL || Long(rec, "r") != o.frameIdR || Long(rec, "skewNs") != o.skewNs || Str(rec, "cls") != o.skew.ToString()) ObservationMismatches++;
        }

        public JsonWriter WriteStats(JsonWriter w)
            => w.Prop("dir", Directory).Prop("events", _events.Count).Prop("cursor", _cursor).Prop("recordedFrame", LastRecordedFrame).Prop("frames", FramesReplayed).Prop("rejected", FramesRejected)
                .Prop("observations", ObservationsReplayed).Prop("recordedObservations", ObservationsRecorded).Prop("mismatches", ObservationMismatches).Prop("depth", DepthReplayed).Prop("images", ImagesLoaded);

        // ---- accessors
        static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v != null ? v.ToString() : null;
        static double ToD(object v) => v is double x ? x : v is long l ? l : v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
        static double Dbl(Dictionary<string, object> d, string k, double def = 0) => d.TryGetValue(k, out var v) && v != null ? ToD(v) : def;
        static long Long(Dictionary<string, object> d, string k, long def = 0) => d.TryGetValue(k, out var v) && v != null ? (v is long l ? l : (long)Math.Round(ToD(v))) : def;
        static bool Bool(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is bool b && b;
        static List<object> Arr(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? v as List<object> : null;
        static Vector3 Vec(Dictionary<string, object> d, string k) { var a = Arr(d, k); return a != null && a.Count >= 3 ? new Vector3((float)ToD(a[0]), (float)ToD(a[1]), (float)ToD(a[2])) : Vector3.zero; }
        static Quaternion Qt(Dictionary<string, object> d, string k) { var a = Arr(d, k); return a != null && a.Count >= 4 ? new Quaternion((float)ToD(a[0]), (float)ToD(a[1]), (float)ToD(a[2]), (float)ToD(a[3])) : Quaternion.identity; }
        static Pose PoseOf(Dictionary<string, object> d, string prefix) => new Pose(Vec(d, prefix + "p"), Qt(d, prefix + "q"));
    }

    /// <summary>Minimal JSON reader (objects → Dictionary, arrays → List, numbers → long/double, no dependencies). Used by replay only.</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return ParseValue(s, ref i); }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ParseValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string num = s.Substring(start, i - start);
            if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
            return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (object)null;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>(); i++;
            while (true)
            {
                Ws(s, ref i); if (i >= s.Length) return d;
                if (s[i] == '}') { i++; return d; }
                if (s[i] == ',') { i++; continue; }
                string key = ParseString(s, ref i); Ws(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[key] = ParseValue(s, ref i);
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var a = new List<object>(); i++;
            while (true)
            {
                Ws(s, ref i); if (i >= s.Length) return a;
                if (s[i] == ']') { i++; return a; }
                if (s[i] == ',') { i++; continue; }
                a.Add(ParseValue(s, ref i));
            }
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder(); if (i < s.Length && s[i] == '"') i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char n = s[i++];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break; case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break; case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                        case 'u': if (i + 4 <= s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; } break;
                        default: sb.Append(n); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
