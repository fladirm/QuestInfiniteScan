using System;
using System.Reflection;
using FinalScan.World;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// Where coherent observations go (contract §3.3, §15): the native measurement ring (FsMeas_Push) and the scan tick
    /// request (FsScan_RequestTick). Kept behind an interface so the sensor layer compiles without
    /// Runtime/Platform/Native/FinalScanHost.cs (owned by another cut) and so host tests can count calls.
    /// </summary>
    public interface ISensorSink
    {
        bool Available { get; }
        /// <summary>FsScan_RequestTick: latest coherent observation only. Returns the FsResult code (0 = ok).</summary>
        int ScanRequestTick(uint observationId);
        /// <summary>FsMeas_Push over a managed array (count items, anchorId). Returns the FsResult code.</summary>
        int MeasPush(FsSurfaceMeasurement[] items, int count, int anchorId);
    }

    /// <summary>Counting sink for tests and for running without the native host.</summary>
    public sealed class NullSensorSink : ISensorSink
    {
        public int Ticks; public uint LastObservationId; public int Pushes; public int PushedItems;
        public bool Available => false;
        public int ScanRequestTick(uint observationId) { Ticks++; LastObservationId = observationId; return 1; }
        public int MeasPush(FsSurfaceMeasurement[] items, int count, int anchorId) { Pushes++; PushedItems += count; return 1; }
    }

    /// <summary>
    /// Default sink: resolves the native bridge by reflection at runtime. Accepts either spelling the host cut may use:
    /// type <c>FinalScan.Platform.Native.FinalScanHost</c> or <c>FinalScanHostNative</c>, methods
    /// <c>ScanRequestTick</c>/<c>RequestScanTick(uint)</c> and <c>MeasPush</c>/<c>PushMeasurements(FsSurfaceMeasurement[], int, int)</c>.
    /// Absence is logged once through <see cref="Log"/> and every call then returns 1 (FS_RESULT_UNAVAILABLE).
    /// </summary>
    public sealed class ReflectionSensorSink : ISensorSink
    {
        static readonly string[] TypeNames = { "FinalScan.Platform.Native.FinalScanHost", "FinalScan.Platform.Native.FinalScanHostNative" };
        static readonly string[] TickNames = { "ScanRequestTick", "RequestScanTick" };
        static readonly string[] PushNames = { "MeasPush", "PushMeasurements" };

        readonly Action<string> _log;
        bool _resolved;
        MethodInfo _tick, _push;
        PropertyInfo _available;
        public string ResolvedType { get; private set; }
        public string ResolveMessage { get; private set; } = "not resolved yet";

        public ReflectionSensorSink(Action<string> log = null) { _log = log; }

        void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var name in TypeNames)
                {
                    try { t = asm.GetType(name, false); } catch (Exception) { t = null; }
                    if (t != null) break;
                }
                if (t != null) break;
            }
            if (t == null) { ResolveMessage = "FS-SENSOR sink: native host bridge type not found (FinalScanHost/FinalScanHostNative); ticks are dropped"; _log?.Invoke(ResolveMessage); return; }
            ResolvedType = t.FullName;
            foreach (var n in TickNames) { _tick = t.GetMethod(n, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(uint) }, null); if (_tick != null) break; }
            foreach (var n in PushNames) { _push = t.GetMethod(n, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(FsSurfaceMeasurement[]), typeof(int), typeof(int) }, null); if (_push != null) break; }
            _available = t.GetProperty("Available", BindingFlags.Public | BindingFlags.Static);
            ResolveMessage = "FS-SENSOR sink: " + ResolvedType + " tick=" + (_tick?.Name ?? "missing") + " push=" + (_push?.Name ?? "missing") + " available=" + Available;
            _log?.Invoke(ResolveMessage);
        }

        public bool Available
        {
            get
            {
                Resolve();
                if (_available == null) return _tick != null;
                try { return (bool)_available.GetValue(null); } catch (Exception) { return false; }
            }
        }

        public int ScanRequestTick(uint observationId)
        {
            Resolve();
            if (_tick == null) return 1;
            try { return (int)_tick.Invoke(null, new object[] { observationId }); } catch (Exception e) { _log?.Invoke("FS-SENSOR sink tick failed: " + e.Message); return 3; }
        }

        public int MeasPush(FsSurfaceMeasurement[] items, int count, int anchorId)
        {
            Resolve();
            if (_push == null) return 1;
            try { return (int)_push.Invoke(null, new object[] { items, count, anchorId }); } catch (Exception e) { _log?.Invoke("FS-SENSOR sink push failed: " + e.Message); return 3; }
        }
    }
}
