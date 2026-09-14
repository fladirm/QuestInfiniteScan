using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FinalScan.Telemetry
{
    /// <summary>
    /// Host-side telemetry sink (C02..C07): one logcat line per record with a fixed prefix, mirrored to
    /// Application.persistentDataPath/host/host-&lt;utc&gt;.jsonl when the file can be opened. Not the probe log:
    /// ProbeLog stays owned by C01; this class only adds a second, independent file.
    /// </summary>
    public sealed class HostLog : IDisposable
    {
        public const string HostPrefix = "FS-HOST-CS";
        public const string SpinPrefix = "FS-SPIN";
        public const string AcceptPrefix = "FS-ACCEPT";

        StreamWriter _file;
        public string FilePath { get; private set; }

        public static HostLog Open(string directory)
        {
            var log = new HostLog();
            try
            {
                Directory.CreateDirectory(directory);
                log.FilePath = Path.Combine(directory, "host-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".jsonl");
                log._file = new StreamWriter(new FileStream(log.FilePath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FinalScan] host jsonl unavailable: " + e.Message);
                log._file = null;
            }
            return log;
        }

        /// <summary>Writes "&lt;prefix&gt; &lt;json&gt;" to logcat and the jsonl file (file gets the JSON only).</summary>
        public void Emit(string prefix, string json)
        {
            Debug.Log(prefix + " " + json);
            try { _file?.WriteLine(json); } catch (Exception) { }
        }

        public void Dispose()
        {
            try { _file?.Dispose(); } catch (Exception) { }
            _file = null;
        }
    }
}
