using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FinalScan.Telemetry
{
    /// <summary>
    /// Probe event sink: every event becomes ONE JSON object written (a) as a jsonl line to a file and
    /// (b) as a logcat line "FS-PROBE {json}" through the injected logger (Debug.Log on device).
    /// Logcat truncates long messages, so documents longer than <see cref="MaxLogcatChars"/> are split into
    /// "FS-PROBE-CHUNK &lt;seq&gt; &lt;i&gt;/&lt;n&gt; &lt;part&gt;" lines which Tools/probe/analyze_probe.py reassembles.
    /// Pure C# (no Unity API) so it can be unit tested; the host injects the clock and the logger.
    /// </summary>
    public sealed class ProbeLog : IDisposable
    {
        public const string LogcatPrefix = "FS-PROBE";
        public const string ChunkPrefix = "FS-PROBE-CHUNK";
        public const int MaxLogcatChars = 3500;

        readonly TextWriter _file;
        readonly Action<string> _logcat;
        readonly Func<double> _clockSeconds;
        readonly Func<DateTime> _utcNow;
        long _seq;

        public string Phase { get; set; } = "init";
        public string FilePath { get; private set; }
        public long Sequence => _seq;

        public ProbeLog(TextWriter file, Action<string> logcat, Func<double> clockSeconds, Func<DateTime> utcNow)
        {
            _file = file;
            _logcat = logcat;
            _clockSeconds = clockSeconds ?? (() => 0.0);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Creates directory/probe-&lt;utc&gt;.jsonl (append, autoflush) and returns the log.</summary>
        public static ProbeLog OpenFile(string directory, DateTime utc, Action<string> logcat, Func<double> clockSeconds)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "probe-" + utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".jsonl");
            var writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            return new ProbeLog(writer, logcat, clockSeconds, () => DateTime.UtcNow) { FilePath = path };
        }

        /// <summary>Emits one event. <paramref name="body"/> adds properties to the (already open) root object.</summary>
        public string Emit(string evt, Action<JsonWriter> body = null)
        {
            long seq = ++_seq;
            var w = new JsonWriter(512);
            w.BeginObject()
             .Prop("seq", seq)
             .Prop("t", _clockSeconds())
             .Prop("utc", _utcNow().ToString("o", CultureInfo.InvariantCulture))
             .Prop("phase", Phase ?? "")
             .Prop("event", evt ?? "");
            try { body?.Invoke(w); }
            catch (Exception e) { w.Prop("bodyError", e.GetType().Name + ": " + e.Message); }
            string json = w.ToString();
            try { _file?.WriteLine(json); } catch (Exception) { /* storage failure must not kill the probe */ }
            if (_logcat != null)
                foreach (var line in LogcatLines(json, seq, MaxLogcatChars)) _logcat(line);
            return json;
        }

        public void Error(string where, Exception e) => Emit("error", w => w.Prop("where", where).Prop("type", e?.GetType().Name).Prop("message", e?.Message).Prop("stack", e?.StackTrace));

        public static IEnumerable<string> LogcatLines(string json, long seq, int maxChars)
        {
            if (json == null) yield break;
            if (maxChars < 16) maxChars = 16;
            if (json.Length <= maxChars) { yield return LogcatPrefix + " " + json; yield break; }
            int n = (json.Length + maxChars - 1) / maxChars;
            for (int i = 0; i < n; i++)
            {
                int start = i * maxChars;
                int len = Math.Min(maxChars, json.Length - start);
                yield return ChunkPrefix + " " + seq.ToString(CultureInfo.InvariantCulture) + " " + (i + 1).ToString(CultureInfo.InvariantCulture) + "/" + n.ToString(CultureInfo.InvariantCulture) + " " + json.Substring(start, len);
            }
        }

        public void Flush() { try { _file?.Flush(); } catch (Exception) { } }
        public void Dispose() { try { _file?.Flush(); _file?.Dispose(); } catch (Exception) { } }
    }
}
