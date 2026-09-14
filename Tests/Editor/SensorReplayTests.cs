using System.Collections.Generic;
using System.Globalization;
using FinalScan.Platform.Sensor;
using NUnit.Framework;
using static FinalScan.Tests.SensorTestKit;

namespace FinalScan.Tests
{
    public class SensorReplayTests
    {
        const long T0 = 2_000_000_000L;

        static List<string> Recording(int frames, long rightOffsetNs)
        {
            var ci = CultureInfo.InvariantCulture;
            var lines = new List<string> { "{\"e\":\"meta\",\"v\":1}" };
            for (int e = 0; e < 2; e++)
            {
                var i = Intrinsics((CameraEye)e); var l = i.lensOffset;
                lines.Add(string.Format(ci, "{{\"e\":\"intr\",\"f\":0,\"eye\":{0},\"fx\":{1},\"fy\":{2},\"cx\":{3},\"cy\":{4},\"w\":1280,\"h\":960,\"sw\":1280,\"sh\":1280,\"lp\":[{5},{6},{7}],\"lq\":[{8},{9},{10},{11}]}}",
                    e, i.fx, i.fy, i.cx, i.cy, l.position.x, l.position.y, l.position.z, l.rotation.x, l.rotation.y, l.rotation.z, l.rotation.w));
            }
            long start = 1_000_000_000L;
            for (int k = 0; k < 8; k++) { long m = start + k * 13_890_000L; lines.Add(string.Format(ci, "{{\"e\":\"clock\",\"f\":{0},\"ob\":{1},\"utc\":{2},\"oa\":{3},\"m\":{4}}}", k, m * 1e-9, UtcTicksFor(m), m * 1e-9 + 5e-6, m)); }
            for (long t = T0 - 100_000_000L; t <= T0 + frames * 20_000_000L + 100_000_000L; t += 13_888_889L)
                lines.Add(string.Format(ci, "{{\"e\":\"pose\",\"f\":{0},\"t\":{1},\"hp\":[0,0,0],\"hq\":[0,0,0,1],\"lp\":[0,0,0],\"lq\":[0,0,0,1],\"rp\":[0,0,0],\"rq\":[0,0,0,1],\"av\":[0,0,0],\"lv\":[0,0,0]}}", 10, t));
            for (int i = 0; i < frames; i++)
            {
                long tl = T0 + i * 20_000_000L, tr = tl + rightOffsetNs;
                lines.Add(string.Format(ci, "{{\"e\":\"frame\",\"f\":{0},\"eye\":0,\"id\":{1},\"mono\":{2},\"utc\":{3},\"on\":{4},\"mn\":{5},\"rt\":true,\"hp\":[0,0,0],\"hq\":[0,0,0,1],\"img\":null}}", 20 + i, i * 2 + 1, tl, UtcTicksFor(tl), (tl + 30_000_000L) * 1e-9, tl + 30_000_000L));
                lines.Add(string.Format(ci, "{{\"e\":\"frame\",\"f\":{0},\"eye\":1,\"id\":{1},\"mono\":{2},\"utc\":{3},\"on\":{4},\"mn\":{5},\"rt\":true,\"hp\":[0,0,0],\"hq\":[0,0,0,1],\"img\":null}}", 20 + i, i * 2 + 2, tr, UtcTicksFor(tr), (tr + 30_000_000L) * 1e-9, tr + 30_000_000L));
            }
            return lines;
        }

        static List<string> Run(List<string> lines, out SensorReplayer replayer)
        {
            var p = new SensorPipeline(new NullSensorSink());
            var obs = new List<string>();
            p.ObservationCreated += o => obs.Add(o.observationId + ":" + o.frameIdL + "/" + o.frameIdR + ":" + o.skewNs + ":" + o.skew + ":" + o.gatePath);
            replayer = SensorReplayer.FromLines(lines);
            replayer.Attach(p, null);
            replayer.RunAll();
            return obs;
        }

        [Test]
        public void Replay_IsDeterministic_AcrossRuns()
        {
            var lines = Recording(30, 6_000_000L);
            var a = Run(lines, out var ra); var b = Run(lines, out var rb);
            Assert.AreEqual(30, ra.FramesReplayed / 2);
            Assert.AreEqual(a.Count, b.Count);
            Assert.Greater(a.Count, 25);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
            StringAssert.Contains(":A", a[0]);
            StringAssert.Contains("MotionCompensate", a[0]);
            Assert.AreEqual(0, ra.ObservationMismatches); // metadata-only recording: nothing to compare against
        }

        [Test]
        public void Replay_ChecksRecordedObservations()
        {
            var lines = Recording(10, 0);
            // record what a correct run produced, then replay with the obs lines present
            var first = Run(lines, out _);
            var withObs = new List<string>(lines);
            foreach (var o in first)
            {
                var parts = o.Split(':'); var ids = parts[1].Split('/');
                withObs.Add("{\"e\":\"obs\",\"f\":99,\"id\":" + parts[0] + ",\"l\":" + ids[0] + ",\"r\":" + ids[1] + ",\"skewNs\":" + parts[2] + ",\"cls\":\"" + parts[3] + "\"}");
            }
            Run(withObs, out var r);
            Assert.AreEqual(first.Count, r.ObservationsRecorded);
            Assert.AreEqual(first.Count, r.ObservationsReplayed);
            Assert.AreEqual(0, r.ObservationMismatches);
            // a tampered record is detected
            withObs[withObs.Count - 1] = withObs[withObs.Count - 1].Replace("\"skewNs\":0", "\"skewNs\":5");
            Run(withObs, out var bad);
            Assert.AreEqual(1, bad.ObservationMismatches);
        }

        [Test]
        public void DepthEvents_ReplayOnHost_WithManagedMatrices()
        {
            var lines = new List<string> { "{\"e\":\"meta\",\"v\":1}",
                "{\"e\":\"depth\",\"f\":5,\"id\":1,\"t\":100,\"rn\":156,\"age\":56,\"near\":0.1,\"far\":null,\"pv\":true,\"fv\":true,\"w\":320,\"h\":320,\"e0p\":[1,2,3],\"e0q\":[0,0.7071068,0,0.7071068],\"fov0\":[-1.15,1.0,1.11,-1.19],\"e1p\":[0,0,0],\"e1q\":[0,0,0,1],\"fov1\":[-1,1,1,-1],\"raw\":null}" };
            var p = new SensorPipeline(new NullSensorSink());
            var r = SensorReplayer.FromLines(lines); r.Attach(p, null); r.RunAll();
            Assert.AreEqual(1, r.DepthReplayed);
            var d = r.LatestDepth;
            Assert.AreEqual(100, d.xrTimeNs);
            Assert.AreEqual(56, d.ageNs);
            Assert.AreEqual(0.1f, d.near, 1e-6f);
            Assert.AreEqual(-1.15f, d.fovTangents[0].x, 1e-6f);
            var m = d.poseMatrices[0];
            Assert.AreEqual(1f, m.m03, 1e-6f); Assert.AreEqual(2f, m.m13, 1e-6f); Assert.AreEqual(3f, m.m23, 1e-6f);
            // 90° about +Y maps +Z to +X: column 2 = (1,0,0)
            Assert.AreEqual(1f, m.m02, 1e-5f); Assert.AreEqual(0f, m.m22, 1e-5f); Assert.AreEqual(1f, m.m33, 1e-6f);
        }

        [Test]
        public void MiniJson_ParsesRecorderShapes()
        {
            var d = (Dictionary<string, object>)MiniJson.Parse("{\"e\":\"frame\",\"id\":12,\"on\":224.85,\"rt\":true,\"img\":null,\"hp\":[1,-2.5,3e2],\"s\":\"a\\\"b\"}");
            Assert.AreEqual("frame", d["e"]);
            Assert.AreEqual(12L, d["id"]);
            Assert.AreEqual(224.85, (double)d["on"], 1e-12);
            Assert.AreEqual(true, d["rt"]);
            Assert.IsNull(d["img"]);
            var a = (List<object>)d["hp"];
            Assert.AreEqual(300.0, (double)a[2], 1e-9);
            Assert.AreEqual("a\"b", d["s"]);
        }
    }
}
