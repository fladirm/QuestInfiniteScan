using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using FinalScan.Telemetry;
using UnityEngine;
using UnityEngine.Rendering;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// Sensor recording (contract §4.7): every raw input of <see cref="SensorPipeline"/> (clock references, pose ring samples,
    /// camera frames with their producer timestamps and runtime-located poses, profiles, intrinsics), the derived observations
    /// (for determinism checks), Environment Depth frames and telemetry go to
    /// <c>persistentDataPath/recordings/&lt;utc&gt;/index.jsonl</c>; images go next to it as raw files
    /// (<c>frames/&lt;eye&gt;_&lt;frameId&gt;.rgba</c> RGBA32, <c>depth/&lt;id&gt;.r32</c> float slices) read back with AsyncGPUReadback,
    /// bounded to <see cref="framesPerSecond"/> per eye and <see cref="MaxInFlight"/> requests; anything over budget is dropped and counted.
    /// File writes run on a worker thread through a bounded queue so the render loop never blocks on storage.
    /// </summary>
    public sealed class SensorRecorder : IDisposable
    {
        public const int MaxInFlight = 2;
        public const int MaxQueuedWrites = 6;
        public const int Version = 1;

        public readonly string Directory;
        public float framesPerSecond;
        readonly SensorPipeline _pipeline; readonly EnvDepthSource _depth; readonly Action<string> _log;
        StreamWriter _index;
        readonly object _indexLock = new object();
        readonly Queue<KeyValuePair<string, byte[]>> _writes = new Queue<KeyValuePair<string, byte[]>>();
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        Thread _writer; volatile bool _stop;
        int _inFlight;
        readonly long[] _lastImageNs = new long[2];
        long _lastDepthImageNs;
        public long Events { get; private set; }
        public long ImagesRequested { get; private set; }
        public long ImagesWritten { get; private set; }
        public long ImagesDroppedBudget { get; private set; }
        public long ImagesDroppedInFlight { get; private set; }
        public long ImagesDroppedQueue { get; private set; }
        public long ImagesFailed { get; private set; }
        public long DepthImages { get; private set; }
        public long BytesWritten { get; private set; }

        public static SensorRecorder Create(SensorPipeline pipeline, EnvDepthSource depth, float framesPerSecond, Action<string> log)
        {
            string dir = Path.Combine(Application.persistentDataPath, "recordings", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
            try { return new SensorRecorder(dir, pipeline, depth, framesPerSecond, log); }
            catch (Exception e) { log?.Invoke("FS-SENSOR recorder unavailable: " + e.Message); return null; }
        }

        public SensorRecorder(string directory, SensorPipeline pipeline, EnvDepthSource depth, float framesPerSecond, Action<string> log)
        {
            Directory = directory; _pipeline = pipeline; _depth = depth; _log = log; this.framesPerSecond = framesPerSecond;
            System.IO.Directory.CreateDirectory(directory);
            System.IO.Directory.CreateDirectory(Path.Combine(directory, "frames"));
            System.IO.Directory.CreateDirectory(Path.Combine(directory, "depth"));
            _index = new StreamWriter(new FileStream(Path.Combine(directory, "index.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = false };
            Emit(new JsonWriter().BeginObject().Prop("e", "meta").Prop("v", Version).Prop("utc", DateTime.UtcNow.ToString("o")).Prop("device", SystemInfo.deviceModel).Prop("unity", Application.unityVersion)
                .Prop("app", Application.version).Prop("fps", framesPerSecond).EndObject());
            _pipeline.ClockReference += OnClock;
            _pipeline.PoseSampled += OnPose;
            _pipeline.FrameCommitted += OnFrame;
            _pipeline.ObservationCreated += OnObservation;
            if (_depth != null) _depth.DepthReceived += OnDepth;
            _writer = new Thread(WriterLoop) { IsBackground = true, Name = "FS sensor recorder" };
            _writer.Start();
            log?.Invoke("FS-SENSOR recorder: " + directory);
        }

        void Emit(JsonWriter w)
        {
            string line = w.ToString();
            lock (_indexLock) { if (_index == null) return; _index.WriteLine(line); Events++; }
        }

        static JsonWriter Vec(JsonWriter w, string key, Vector3 v) => w.BeginArray(key).Value(v.x).Value(v.y).Value(v.z).EndArray();
        static JsonWriter Quat(JsonWriter w, string key, Quaternion q) => w.BeginArray(key).Value(q.x).Value(q.y).Value(q.z).Value(q.w).EndArray();
        static JsonWriter PoseJ(JsonWriter w, string prefix, in Pose p) { Vec(w, prefix + "p", p.position); return Quat(w, prefix + "q", p.rotation); }

        void OnClock(double ob, long utc, double oa, long m) => Emit(new JsonWriter().BeginObject().Prop("e", "clock").Prop("f", Time.frameCount).Prop("ob", ob).Prop("utc", utc).Prop("oa", oa).Prop("m", m).EndObject());

        void OnPose(PoseSample s)
        {
            var w = new JsonWriter(320).BeginObject().Prop("e", "pose").Prop("f", Time.frameCount).Prop("t", s.xrTimeNs);
            PoseJ(w, "h", s.head); PoseJ(w, "l", s.eyeLeft); PoseJ(w, "r", s.eyeRight); Vec(w, "av", s.angularVelocity); Vec(w, "lv", s.linearVelocity);
            Emit(w.EndObject());
        }

        public void WriteIntrinsics(int eye, in CameraIntrinsicsData i)
        {
            var w = new JsonWriter().BeginObject().Prop("e", "intr").Prop("f", Time.frameCount).Prop("eye", eye).Prop("fx", i.fx).Prop("fy", i.fy).Prop("cx", i.cx).Prop("cy", i.cy)
                .Prop("w", i.width).Prop("h", i.height).Prop("sw", i.sensorWidth).Prop("sh", i.sensorHeight);
            PoseJ(w, "l", i.lensOffset);
            Emit(w.EndObject());
        }

        public void WriteProfile(CaptureProfile p, string reason) => Emit(new JsonWriter().BeginObject().Prop("e", "profile").Prop("f", Time.frameCount).Prop("p", p.ToString()).Prop("reason", reason).EndObject());
        public void WriteTelemetry(string json) => Emit(new JsonWriter(json.Length + 64).BeginObject().Prop("e", "telemetry").Prop("f", Time.frameCount).PropRaw("json", json).EndObject());

        void OnFrame(CameraFrameLease l)
        {
            string img = null;
            long now = l.receiveXrTimeNs > 0 ? l.receiveXrTimeNs : l.xrTimeNs;
            long minGap = framesPerSecond > 0 ? (long)(1e9 / framesPerSecond) : long.MaxValue;
            if (l.texture != null && framesPerSecond > 0)
            {
                if (now - _lastImageNs[(int)l.eye] < minGap) ImagesDroppedBudget++;
                else if (_inFlight >= MaxInFlight) ImagesDroppedInFlight++;
                else
                {
                    _lastImageNs[(int)l.eye] = now;
                    img = "frames/" + (l.eye == CameraEye.Left ? "L" : "R") + "_" + l.frameId + ".rgba";
                    RequestImage(l.texture, img, TextureFormat.RGBA32);
                }
            }
            var w = new JsonWriter(512).BeginObject().Prop("e", "frame").Prop("f", Time.frameCount).Prop("eye", (int)l.eye).Prop("id", l.frameId)
                .Prop("mono", l.sourceMonoNs).Prop("utc", l.sourceUtcTicks).Prop("on", l.receiveXrTimeNs / 1e9).Prop("mn", l.receiveMonotonicNs)
                .Prop("rt", l.poseFromRuntime).Prop("pv", l.poseValid).Prop("t", l.xrTimeNs).Prop("u", l.uncertaintyNs).Prop("g", l.gatePath.ToString()).Prop("prof", l.profile.ToString());
            PoseJ(w, "h", l.headPose); PoseJ(w, "c", l.cameraPose);
            if (l.texture != null) w.Prop("w", l.texture.width).Prop("h", l.texture.height);
            w.Prop("img", img);
            Emit(w.EndObject());
        }

        void OnObservation(StereoObservation o)
        {
            var w = new JsonWriter(512).BeginObject().Prop("e", "obs").Prop("f", Time.frameCount).Prop("id", (long)o.observationId).Prop("l", o.frameIdL).Prop("r", o.frameIdR)
                .Prop("tl", o.xrTimeNsL).Prop("tr", o.xrTimeNsR).Prop("skewNs", o.skewNs).Prop("cls", o.skew.ToString()).Prop("conf", o.confidence).Prop("ang", o.headAngularDegPerSec)
                .Prop("lin", o.headLinearMetersPerSec).Prop("motion", o.motion.ToString()).Prop("geom", o.geometryEvidence).Prop("baseline", o.baselineMeters).Prop("g", o.gatePath.ToString());
            PoseJ(w, "x", o.extrinsicsRightInLeft);
            Emit(w.EndObject());
        }

        void OnDepth(DepthFrame d)
        {
            string raw = null;
            long minGap = framesPerSecond > 0 ? (long)(1e9 / framesPerSecond) : long.MaxValue;
            if (d.texture != null && framesPerSecond > 0 && d.receiveXrTimeNs - _lastDepthImageNs >= minGap && _inFlight < MaxInFlight)
            {
                _lastDepthImageNs = d.receiveXrTimeNs;
                raw = "depth/" + d.frameId + ".r32";
                RequestDepth(d.texture, raw);
            }
            var w = new JsonWriter(640).BeginObject().Prop("e", "depth").Prop("f", Time.frameCount).Prop("id", d.frameId).Prop("t", d.xrTimeNs).Prop("rn", d.receiveXrTimeNs).Prop("age", d.ageNs)
                .Prop("near", d.near).Prop("far", d.far).Prop("pv", d.posesValid).Prop("fv", d.fovsValid).Prop("w", d.width).Prop("h", d.height);
            for (int e = 0; e < 2; e++)
            {
                PoseJ(w, "e" + e, d.poses[e]);
                var f = d.fovTangents[e]; w.BeginArray("fov" + e).Value(f.x).Value(f.y).Value(f.z).Value(f.w).EndArray();
            }
            w.Prop("raw", raw);
            Emit(w.EndObject());
        }

        void RequestImage(Texture tex, string rel, TextureFormat fmt)
        {
            try
            {
                _inFlight++; ImagesRequested++;
                AsyncGPUReadback.Request(tex, 0, fmt, r => Complete(r, rel));
            }
            catch (Exception e) { _inFlight--; ImagesFailed++; if (ImagesFailed < 3) _log?.Invoke("FS-SENSOR recorder readback failed: " + e.Message); }
        }

        void RequestDepth(Texture tex, string rel)
        {
            try
            {
                int slices = tex is RenderTexture rt ? Math.Max(1, rt.volumeDepth) : tex is Texture2DArray a ? a.depth : 1;
                _inFlight++; ImagesRequested++;
                AsyncGPUReadback.Request(tex, 0, 0, tex.width, 0, tex.height, 0, slices, TextureFormat.RFloat, r => { Complete(r, rel); if (!r.hasError) DepthImages++; });
            }
            catch (Exception e) { _inFlight--; ImagesFailed++; if (ImagesFailed < 3) _log?.Invoke("FS-SENSOR recorder depth readback failed: " + e.Message); }
        }

        void Complete(AsyncGPUReadbackRequest r, string rel)
        {
            _inFlight = Math.Max(0, _inFlight - 1);
            if (r.hasError) { ImagesFailed++; return; }
            byte[] data;
            try { data = r.GetData<byte>().ToArray(); } catch (Exception) { ImagesFailed++; return; }
            lock (_writes)
            {
                if (_writes.Count >= MaxQueuedWrites) { ImagesDroppedQueue++; return; }
                _writes.Enqueue(new KeyValuePair<string, byte[]>(rel, data));
            }
            _signal.Set();
        }

        void WriterLoop()
        {
            while (!_stop)
            {
                KeyValuePair<string, byte[]> item; bool have = false;
                lock (_writes) { if (_writes.Count > 0) { item = _writes.Dequeue(); have = true; } else item = default; }
                if (!have) { _signal.WaitOne(200); continue; }
                try { File.WriteAllBytes(Path.Combine(Directory, item.Key), item.Value); ImagesWritten++; BytesWritten += item.Value.Length; }
                catch (Exception e) { ImagesFailed++; if (ImagesFailed < 3) _log?.Invoke("FS-SENSOR recorder write failed: " + e.Message); }
            }
        }

        /// <summary>Flushes the index periodically (called once per host frame).</summary>
        public void Tick(long nowXrNs)
        {
            if ((Events & 63) == 0) { lock (_indexLock) { try { _index?.Flush(); } catch (Exception) { } } }
        }

        public JsonWriter WriteStats(JsonWriter w)
            => w.Prop("dir", Directory).Prop("events", Events).Prop("imagesRequested", ImagesRequested).Prop("imagesWritten", ImagesWritten).Prop("droppedBudget", ImagesDroppedBudget)
                .Prop("droppedInFlight", ImagesDroppedInFlight).Prop("droppedQueue", ImagesDroppedQueue).Prop("failed", ImagesFailed).Prop("depthImages", DepthImages).Prop("mb", BytesWritten / 1048576.0);

        public void Dispose()
        {
            _pipeline.ClockReference -= OnClock; _pipeline.PoseSampled -= OnPose; _pipeline.FrameCommitted -= OnFrame; _pipeline.ObservationCreated -= OnObservation;
            if (_depth != null) _depth.DepthReceived -= OnDepth;
            _stop = true; _signal.Set();
            try { _writer?.Join(2000); } catch (Exception) { }
            lock (_indexLock) { try { _index?.Flush(); _index?.Dispose(); } catch (Exception) { } _index = null; }
        }
    }
}
