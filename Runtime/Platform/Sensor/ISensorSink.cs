using FinalScan.Platform.Native;
using FinalScan.World;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// Where coherent observations go (contract §3.3, §15): the native measurement ring (FsMeas_Push) and the scan tick
    /// request (FsScan_RequestTick). An interface so the replayer and the EditMode tests can count calls.
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

    /// <summary>Production sink: the native host bridge (FinalScanHostNative). Every call returns the FsResult code.</summary>
    public sealed class NativeSensorSink : ISensorSink
    {
        public bool Available => FinalScanHostNative.Available;
        public int ScanRequestTick(uint observationId) => FinalScanHostNative.RequestScanTick(observationId);
        public int MeasPush(FsSurfaceMeasurement[] items, int count, int anchorId) => FinalScanHostNative.PushMeasurements(items, count, anchorId);
    }
}
