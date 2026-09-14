using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FinalScan.Telemetry;
using NUnit.Framework;

namespace FinalScan.Tests
{
    public class ProbeLogTests
    {
        [Test]
        public void JsonWriter_EscapesStringsAndFormatsNumbers()
        {
            var w = new JsonWriter();
            w.BeginObject().Prop("s", "a\"b\\c\n\t" + (char)1 + "é").Prop("d", 1.5).Prop("nan", double.NaN).Prop("inf", double.PositiveInfinity)
             .Prop("i", 42L).Prop("b", true).PropNull("n").Prop("small", 1e-7).Prop("f", 0.25f).EndObject();
            string json = w.ToString();
            Assert.AreEqual("{\"s\":\"a\\\"b\\\\c\\n\\t\\u0001é\",\"d\":1.5,\"nan\":null,\"inf\":null,\"i\":42,\"b\":true,\"n\":null,\"small\":1E-07,\"f\":0.25}", json);
            Assert.AreEqual("\"x\\ry\"", JsonWriter.Escape("x\ry"));
        }

        [Test]
        public void JsonWriter_NestsObjectsAndArraysWithCorrectCommas()
        {
            var w = new JsonWriter();
            w.BeginObject().BeginArray("xs").Value(1).Value(2.5).Value("s").ValueNull().Value(false).BeginObject().Prop("k", 1).EndObject().EndArray()
             .BeginObject("o").EndObject().BeginArray("e").EndArray().PropRaw("raw", "{\"z\":1}").PropRaw("rawEmpty", "").EndObject();
            Assert.AreEqual("{\"xs\":[1,2.5,\"s\",null,false,{\"k\":1}],\"o\":{},\"e\":[],\"raw\":{\"z\":1},\"rawEmpty\":null}", w.ToString());
            var arr = new JsonWriter().BeginArray().Values(new[] { 1.0, 2.0 }).Values(new List<long> { 3 }).Values(new[] { "a" }).EndArray().ToString();
            Assert.AreEqual("[1,2,3,\"a\"]", arr);
        }

        [Test]
        public void JsonWriter_ToStringAutoClosesOpenObjects()
        {
            var w = new JsonWriter().BeginObject().Prop("a", 1).BeginObject("b");
            Assert.AreEqual("{\"a\":1,\"b\":{}}", w.ToString());
            Assert.AreEqual(2, w.Depth);
        }

        [Test]
        public void ProbeLog_WritesJsonlLineAndLogcatLineWithCommonFields()
        {
            var file = new StringWriter();
            var logcat = new List<string>();
            double t = 12.5;
            var log = new ProbeLog(file, logcat.Add, () => t, () => new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc)) { Phase = "boot" };
            string json = log.Emit("hello", w => w.Prop("x", 1).Prop("s", "y"));
            Assert.AreEqual("{\"seq\":1,\"t\":12.5,\"utc\":\"2026-09-14T10:00:00.0000000Z\",\"phase\":\"boot\",\"event\":\"hello\",\"x\":1,\"s\":\"y\"}", json);
            Assert.AreEqual(json + Environment.NewLine, file.ToString());
            Assert.AreEqual(1, logcat.Count);
            Assert.AreEqual("FS-PROBE " + json, logcat[0]);
            log.Emit("second");
            Assert.AreEqual(2, log.Sequence);
            Assert.IsTrue(logcat[1].StartsWith("FS-PROBE {\"seq\":2,"));
            Assert.AreEqual(2, file.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [Test]
        public void ProbeLog_BodyExceptionDoesNotBreakTheLine()
        {
            var logcat = new List<string>();
            var log = new ProbeLog(null, logcat.Add, () => 0, () => DateTime.UnixEpoch);
            string json = log.Emit("boom", w => { w.Prop("before", 1); throw new InvalidOperationException("x"); });
            StringAssert.Contains("\"before\":1", json);
            StringAssert.Contains("\"bodyError\":\"InvalidOperationException: x\"", json);
            StringAssert.EndsWith("}", json);
        }

        [Test]
        public void ProbeLog_ChunksLongLogcatLinesAndTheyReassemble()
        {
            string json = "{\"a\":\"" + new string('x', 10000) + "\"}";
            var lines = ProbeLog.LogcatLines(json, 7, 3500).ToList();
            Assert.AreEqual(3, lines.Count);
            Assert.IsTrue(lines[0].StartsWith("FS-PROBE-CHUNK 7 1/3 "));
            Assert.IsTrue(lines[2].StartsWith("FS-PROBE-CHUNK 7 3/3 "));
            string joined = string.Concat(lines.Select(l => l.Substring(l.IndexOf("/3 ", StringComparison.Ordinal) + 3)));
            Assert.AreEqual(json, joined);
            var single = ProbeLog.LogcatLines("{}", 1, 3500).ToList();
            Assert.AreEqual(1, single.Count); Assert.AreEqual("FS-PROBE {}", single[0]);
        }

        [Test]
        public void ProbeLog_OpenFileCreatesJsonlInDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fs-probe-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var log = ProbeLog.OpenFile(dir, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), null, () => 1))
                {
                    Assert.AreEqual(Path.Combine(dir, "probe-20260102T030405Z.jsonl"), log.FilePath);
                    log.Emit("a"); log.Emit("b");
                }
                var lines = File.ReadAllLines(Path.Combine(dir, "probe-20260102T030405Z.jsonl"));
                Assert.AreEqual(2, lines.Length);
                StringAssert.Contains("\"event\":\"b\"", lines[1]);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void SampleStats_PercentilesHistogramAndJson()
        {
            var s = new SampleStats();
            for (int i = 1; i <= 100; i++) s.Add(i);
            s.Add(double.NaN);
            Assert.AreEqual(100, s.Count); Assert.AreEqual(1, s.Min); Assert.AreEqual(100, s.Max); Assert.AreEqual(50.5, s.Mean, 1e-12);
            Assert.AreEqual(50, s.Percentile(50)); Assert.AreEqual(99, s.Percentile(99)); Assert.AreEqual(100, s.Percentile(100)); Assert.AreEqual(1, s.Percentile(0));
            Assert.AreEqual(29.011, s.StdDev(), 1e-3);
            var h = s.Histogram(10, 5);
            CollectionAssert.AreEqual(new[] { 9, 10, 10, 10, 10, 51 }, h);
            var w = new JsonWriter().BeginObject();
            s.WriteTo(w, "st"); s.WriteHistogram(w, "h", 10, 5); w.EndObject();
            string json = w.ToString();
            StringAssert.Contains("\"st\":{\"n\":100,\"min\":1,\"max\":100,\"mean\":50.5,\"p50\":50,\"p90\":90,\"p99\":99,", json);
            StringAssert.Contains("\"h\":{\"binWidth\":10,\"origin\":0,\"bins\":5,\"counts\":[9,10,10,10,10],\"overflow\":51}", json);
            s.Reset();
            Assert.AreEqual(0, s.Count); Assert.IsTrue(double.IsNaN(s.Percentile(50))); Assert.IsTrue(double.IsNaN(s.Mean));
        }

        [Test]
        public void SampleStats_CapsStoredSamplesButCountsAll()
        {
            var s = new SampleStats(16);
            for (int i = 0; i < 100; i++) s.Add(i);
            Assert.AreEqual(100, s.Count); Assert.AreEqual(16, s.Stored); Assert.AreEqual(99, s.Max); Assert.AreEqual(99, s.Last);
        }
    }
}
