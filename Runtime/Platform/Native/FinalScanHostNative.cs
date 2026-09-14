using System;
using System.Runtime.InteropServices;
using System.Text;
using FinalScan.World;

namespace FinalScan.Platform.Native
{
    /// <summary>
    /// P/Invoke surface of libFinalScanNative.so for ABI 2 (C02..C08). Mirrors Native~/include/finalscan_host_api.h
    /// function by function; packed record layouts come from the generated <see cref="FinalScan.World.WorldAbi"/>.
    /// Every call is main-thread safe. Outside an Android player every entry point is a stub that returns
    /// <see cref="ResultUnavailable"/> so the host degrades to "native absent" instead of throwing.
    /// </summary>
    public static class FinalScanHostNative
    {
        public const int AbiVersion = 2;
        const string Lib = "FinalScanNative";

        // FsResult (finalscan_native_api.h)
        public const int ResultOk = 0;
        public const int ResultUnavailable = 1;
        public const int ResultInvalid = 2;
        public const int ResultDevice = 3;
        public const int ResultBusy = 4;

        public enum HostStatus { Uninitialized = 0, WarmingUp = 1, Ready = 2, DeviceLost = 3, Quarantined = 4, Shutdown = 5 }

        public enum JobClass { Publish = 0, Scan = 1, InnerResidency = 2, Appearance = 3, Cold = 4 }
        public const int JobClassCount = 5;

        /// <summary>FsHostRenderEvent: ids passed to CommandBuffer.IssuePluginEvent(RenderEventFunc, id).</summary>
        public enum RenderEvent { FrameBegin = 100, FrameEnd = 101, WarmupStep = 102 }

        /// <summary>FsWorld_CreateSynthetic kinds (header: 0 room box, 1 corridor, 2 stairs, 3 thin wall; 4 reserved for the dense flower-shop benchmark).</summary>
        public enum SyntheticKind { RoomBox = 0, Corridor = 1, Stairs = 2, ThinWall = 3, Dense = 4 }

        /// <summary>FsRender_SetMode values (contract §13.5 render modes).</summary>
        public enum RenderModeId { Scan = 0, XRay = 1, Plan = 2 }

        /// <summary>Byte-identical mirror of FsHostConfig (100 B). Fill through <see cref="HostConfig.Create"/>.</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public unsafe struct HostConfig
        {
            public uint structSize;
            public uint residentPageSlots;
            public uint surfelsPerPage;
            public uint pageHashCapacity;
            public uint measurementRingCapacity;
            public uint drawRecordCapacity;
            public uint frameBudgetUs;
            public fixed uint inFlightMax[JobClassCount];
            public fixed uint quantumUs[JobClassCount];
            public uint useScannerQueue;
            public uint enableSyntheticWorld;
            public fixed uint reserved[6];

            public const int SizeBytes = 4 * (7 + JobClassCount + JobClassCount + 2 + 6);

            public static HostConfig Create()
            {
                var c = new HostConfig { structSize = (uint)SizeBytes };
                return c;
            }

            public void SetInFlightMax(JobClass cls, uint value) { fixed (uint* p = inFlightMax) p[(int)cls] = value; }
            public void SetQuantumUs(JobClass cls, uint value) { fixed (uint* p = quantumUs) p[(int)cls] = value; }
            public uint GetInFlightMax(JobClass cls) { fixed (uint* p = inFlightMax) return p[(int)cls]; }
            public uint GetQuantumUs(JobClass cls) { fixed (uint* p = quantumUs) return p[(int)cls]; }
        }

        /// <summary>FsSched_GetClassStats layout.</summary>
        public const int ClassStatsCount = 8;      // submitted, retired, deferred, failed, gpuUsTotal, gpuUsLast, inFlight, quantumUs
        /// <summary>FsResidency_GetZoneStats layout.</summary>
        public const int ZoneStatsCount = 8;       // innerPages, warmPages, prefetchPages, requestsThisFrame, orientationRequests, evictions, loads, stalls
        /// <summary>FsRender_GetLastCullStats layout.</summary>
        public const int CullStatsCount = 4;       // culledPages, visibleSurfels, drawRecords, cullGpuUs

#if UNITY_ANDROID && !UNITY_EDITOR
        [DllImport(Lib)] static extern int FsHost_GetAbiVersion();
        [DllImport(Lib)] static extern int FsHost_Init(ref HostConfig cfg);
        [DllImport(Lib)] static extern int FsHost_Shutdown();
        [DllImport(Lib)] static extern int FsHost_GetStatus();
        [DllImport(Lib)] static extern int FsHost_GetTelemetryJson(byte[] buf, int cap);
        [DllImport(Lib)] static extern long FsHost_GetCounter(int counter);
        [DllImport(Lib)] static extern IntPtr FsHost_GetRenderEventFunc();
        [DllImport(Lib)] static extern int FsHost_SetStorageRoot(byte[] utf8NulTerminated);

        [DllImport(Lib)] static extern int FsSched_SetFrameBudgetUs(uint us);
        [DllImport(Lib)] static extern int FsSched_SetFrameHint(double predictedDisplayTimeSec, float[] headPos, float[] headVel, float[] headRot);
        [DllImport(Lib)] static extern int FsSched_GetClassStats(int cls, long[] out8);

        [DllImport(Lib)] static extern unsafe int FsMeas_Push(FsSurfaceMeasurement* items, int count, int anchorId);
        [DllImport(Lib)] static extern int FsScan_RequestTick(uint observationId);

        [DllImport(Lib)] static extern int FsWorld_CreateSynthetic(int kind, float extentM, float surfelSpacingM, uint seed);
        [DllImport(Lib)] static extern int FsWorld_GetPageCount(out int resident, out int logical);
        [DllImport(Lib)] static extern int FsWorld_GetSurfelCount(out long frontTotal, out long backTotal);
        [DllImport(Lib)] static extern uint FsWorld_GetFrontGeneration();
        [DllImport(Lib)] static extern int FsWorld_SetAnchorTransform(int anchorId, float[] worldFromAnchor16);
        [DllImport(Lib)] static extern int FsWorld_Erase(float[] center3, float radius, int anchorId);

        [DllImport(Lib)] static extern int FsResidency_SetCenter(float[] predictedPos3, float innerRadius, float warmRadius, float prefetchRadius);
        [DllImport(Lib)] static extern int FsResidency_GetZoneStats(long[] out8);

        [DllImport(Lib)] static extern int FsRender_RegisterBuffers(IntPtr unityDrawRecordBuffer, uint drawRecordBytes, IntPtr unityIndirectArgsBuffer);
        [DllImport(Lib)] static extern int FsRender_SetView(float[] viewL16, float[] projL16, float[] viewR16, float[] projR16, float[] headPos3);
        [DllImport(Lib)] static extern int FsRender_GetLastCullStats(long[] out4);
        [DllImport(Lib)] static extern int FsRender_SetPrevDepth(IntPtr unityDepthTexture, uint width, uint height, uint layers, float[] viewL16, float[] projL16, float[] viewR16, float[] projR16);
        [DllImport(Lib)] static extern int FsRender_SetEnvDepth(IntPtr unityDepthTextureArray, uint width, uint height, float[] poseL16, float[] poseR16, float[] fovL4, float[] fovR4, float nearZ, float farZ, long xrTimeNs);
        [DllImport(Lib)] static extern int FsRender_SetLodPolicy(float fovealErrorPx, float peripheralErrorPx, float predictionMarginDeg, uint screenWorkBudget);
        [DllImport(Lib)] static extern int FsRender_SetGpuHeadroomUs(int headroomUs);
        [DllImport(Lib)] static extern int FsRender_SetMode(int mode);
        // Twin of FsRender_SetEnvDepth exported by the measurement module (same signature, same frame, same handle).
        [DllImport(Lib)] static extern int FsMeas_SetEnvDepth(IntPtr unityDepthTextureArray, uint width, uint height, float[] poseL16, float[] poseR16, float[] fovL4, float[] fovR4, float nearZ, float farZ, long xrTimeNs);

        static int s_abi = int.MinValue;
        /// <summary>True when the plugin loads and reports ABI 2. Cached after the first successful probe.</summary>
        public static bool Available
        {
            get
            {
                if (s_abi == int.MinValue)
                {
                    try { s_abi = FsHost_GetAbiVersion(); }
                    catch (DllNotFoundException) { s_abi = -1; }
                    catch (EntryPointNotFoundException) { s_abi = -2; }
                }
                return s_abi == AbiVersion;
            }
        }
        public static int ReportedAbiVersion { get { bool _ = Available; return s_abi; } }

        public static int Init(ref HostConfig cfg) => FsHost_Init(ref cfg);
        public static int Shutdown() => FsHost_Shutdown();
        public static HostStatus Status => (HostStatus)FsHost_GetStatus();
        public static string TelemetryJson() => ReadJson(FsHost_GetTelemetryJson);
        public static long GetCounter(FsCounter counter) => FsHost_GetCounter((int)counter);
        public static IntPtr RenderEventFunc => FsHost_GetRenderEventFunc();
        public static int SetStorageRoot(string path)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(path ?? string.Empty);
            var z = new byte[utf8.Length + 1];
            Buffer.BlockCopy(utf8, 0, z, 0, utf8.Length);
            return FsHost_SetStorageRoot(z);
        }

        public static int SetFrameBudgetUs(uint us) => FsSched_SetFrameBudgetUs(us);
        public static int SetFrameHint(double predictedDisplayTimeSec, float[] headPos3, float[] headVel3, float[] headRot4) => FsSched_SetFrameHint(predictedDisplayTimeSec, headPos3, headVel3, headRot4);
        public static int GetClassStats(JobClass cls, long[] out8) => FsSched_GetClassStats((int)cls, out8);

        public static unsafe int PushMeasurements(FsSurfaceMeasurement* items, int count, int anchorId) => FsMeas_Push(items, count, anchorId);
        public static unsafe int PushMeasurements(FsSurfaceMeasurement[] items, int count, int anchorId)
        {
            if (items == null || count <= 0) return ResultInvalid;
            fixed (FsSurfaceMeasurement* p = items) return FsMeas_Push(p, Math.Min(count, items.Length), anchorId);
        }
        public static int RequestScanTick(uint observationId)
        {
            if (!ScanRequestsEnabled) { ScanTicksGated++; return ResultBusy; }
            return FsScan_RequestTick(observationId);
        }

        public static int CreateSyntheticWorld(SyntheticKind kind, float extentM, float surfelSpacingM, uint seed) => FsWorld_CreateSynthetic((int)kind, extentM, surfelSpacingM, seed);
        public static int GetPageCount(out int resident, out int logical) => FsWorld_GetPageCount(out resident, out logical);
        public static int GetSurfelCount(out long frontTotal, out long backTotal) => FsWorld_GetSurfelCount(out frontTotal, out backTotal);
        public static uint FrontGeneration => FsWorld_GetFrontGeneration();
        public static int SetAnchorTransform(int anchorId, float[] worldFromAnchorColumnMajor16) => FsWorld_SetAnchorTransform(anchorId, worldFromAnchorColumnMajor16);
        public static int Erase(float[] center3, float radius, int anchorId) => FsWorld_Erase(center3, radius, anchorId);

        public static int SetResidencyCenter(float[] predictedPos3, float innerRadius, float warmRadius, float prefetchRadius) => FsResidency_SetCenter(predictedPos3, innerRadius, warmRadius, prefetchRadius);
        public static int GetZoneStats(long[] out8) => FsResidency_GetZoneStats(out8);

        public static int RegisterRenderBuffers(IntPtr drawRecordBuffer, uint drawRecordBytes, IntPtr indirectArgsBuffer) => FsRender_RegisterBuffers(drawRecordBuffer, drawRecordBytes, indirectArgsBuffer);
        public static int SetView(float[] viewL16, float[] projL16, float[] viewR16, float[] projR16, float[] headPos3) => FsRender_SetView(viewL16, projL16, viewR16, projR16, headPos3);
        public static int GetLastCullStats(long[] out4) => FsRender_GetLastCullStats(out4);
        public static int SetPrevDepth(IntPtr depthTexture, uint width, uint height, uint layers, float[] viewL16, float[] projL16, float[] viewR16, float[] projR16)
            => FsRender_SetPrevDepth(depthTexture, width, height, layers, viewL16, projL16, viewR16, projR16);
        public static int SetEnvDepth(IntPtr depthTextureArray, uint width, uint height, float[] poseL16, float[] poseR16, float[] fovL4, float[] fovR4, float nearZ, float farZ, long xrTimeNs)
            => FsRender_SetEnvDepth(depthTextureArray, width, height, poseL16, poseR16, fovL4, fovR4, nearZ, farZ, xrTimeNs);
        public static int SetLodPolicy(float fovealErrorPx, float peripheralErrorPx, float predictionMarginDeg, uint screenWorkBudget)
            => FsRender_SetLodPolicy(fovealErrorPx, peripheralErrorPx, predictionMarginDeg, screenWorkBudget);
        public static int SetGpuHeadroomUs(int headroomUs) => FsRender_SetGpuHeadroomUs(headroomUs);

        /// <summary>Measurement front-end twin of <see cref="SetEnvDepth"/> (FsMeas_SetEnvDepth).</summary>
        public static int SetMeasEnvDepth(IntPtr depthTextureArray, uint width, uint height, float[] poseL16, float[] poseR16, float[] fovL4, float[] fovR4, float nearZ, float farZ, long xrTimeNs)
            => FsMeas_SetEnvDepth(depthTextureArray, width, height, poseL16, poseR16, fovL4, fovR4, nearZ, farZ, xrTimeNs);
        public static int SetMode(RenderModeId mode) => FsRender_SetMode((int)mode);
#else
        public static bool Available => false;
        public static int ReportedAbiVersion => 0;
        public static int Init(ref HostConfig cfg) => ResultUnavailable;
        public static int Shutdown() => ResultUnavailable;
        public static HostStatus Status => HostStatus.Uninitialized;
        public static string TelemetryJson() => "{}";
        public static long GetCounter(FsCounter counter) => 0;
        public static IntPtr RenderEventFunc => IntPtr.Zero;
        public static int SetStorageRoot(string path) => ResultUnavailable;
        public static int SetFrameBudgetUs(uint us) => ResultUnavailable;
        public static int SetFrameHint(double predictedDisplayTimeSec, float[] headPos3, float[] headVel3, float[] headRot4) => ResultUnavailable;
        public static int GetClassStats(JobClass cls, long[] out8) => ResultUnavailable;
        public static unsafe int PushMeasurements(FsSurfaceMeasurement* items, int count, int anchorId) => ResultUnavailable;
        public static int PushMeasurements(FsSurfaceMeasurement[] items, int count, int anchorId) => ResultUnavailable;
        public static int RequestScanTick(uint observationId) { if (!ScanRequestsEnabled) { ScanTicksGated++; return ResultBusy; } return ResultUnavailable; }
        public static int CreateSyntheticWorld(SyntheticKind kind, float extentM, float surfelSpacingM, uint seed) => ResultUnavailable;
        public static int GetPageCount(out int resident, out int logical) { resident = 0; logical = 0; return ResultUnavailable; }
        public static int GetSurfelCount(out long frontTotal, out long backTotal) { frontTotal = 0; backTotal = 0; return ResultUnavailable; }
        public static uint FrontGeneration => 0;
        public static int SetAnchorTransform(int anchorId, float[] worldFromAnchorColumnMajor16) => ResultUnavailable;
        public static int Erase(float[] center3, float radius, int anchorId) => ResultUnavailable;
        public static int SetResidencyCenter(float[] predictedPos3, float innerRadius, float warmRadius, float prefetchRadius) => ResultUnavailable;
        public static int GetZoneStats(long[] out8) => ResultUnavailable;
        public static int RegisterRenderBuffers(IntPtr drawRecordBuffer, uint drawRecordBytes, IntPtr indirectArgsBuffer) => ResultUnavailable;
        public static int SetView(float[] viewL16, float[] projL16, float[] viewR16, float[] projR16, float[] headPos3) => ResultUnavailable;
        public static int GetLastCullStats(long[] out4) => ResultUnavailable;
        public static int SetPrevDepth(IntPtr depthTexture, uint width, uint height, uint layers, float[] viewL16, float[] projL16, float[] viewR16, float[] projR16) => ResultUnavailable;
        public static int SetEnvDepth(IntPtr depthTextureArray, uint width, uint height, float[] poseL16, float[] poseR16, float[] fovL4, float[] fovR4, float nearZ, float farZ, long xrTimeNs) => ResultUnavailable;
        public static int SetLodPolicy(float fovealErrorPx, float peripheralErrorPx, float predictionMarginDeg, uint screenWorkBudget) => ResultUnavailable;
        public static int SetGpuHeadroomUs(int headroomUs) => ResultUnavailable;
        public static int SetMode(RenderModeId mode) => ResultUnavailable;
        public static int SetMeasEnvDepth(IntPtr depthTextureArray, uint width, uint height, float[] poseL16, float[] poseR16, float[] fovL4, float[] fovR4, float nearZ, float farZ, long xrTimeNs) => ResultUnavailable;
#endif

        /// <summary>
        /// START/STOP SCAN gate (C22): while false every FsScan_RequestTick is refused with <see cref="ResultBusy"/> and
        /// counted in <see cref="ScanTicksGated"/>, whichever caller (sensor sink, tests) issues it.
        /// Owned by FinalScan.Host.FinalScanHost.ScanEnabled. Defaults to true so a host without UI scans as before.
        /// </summary>
        public static bool ScanRequestsEnabled { get; set; } = true;
        public static long ScanTicksGated { get; private set; }

        delegate int JsonGetter(byte[] buf, int cap);
        static byte[] s_jsonBuf = new byte[16384];
        static string ReadJson(JsonGetter getter)
        {
            byte[] buf = s_jsonBuf;
            int need = getter(buf, buf.Length);
            if (need > buf.Length) { buf = s_jsonBuf = new byte[need + 1]; need = getter(buf, buf.Length); }
            int len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = Math.Min(need, buf.Length);
            return Encoding.UTF8.GetString(buf, 0, len);
        }

        /// <summary>Throws when the managed FsHostConfig drifts from the C header size (call once at startup).</summary>
        public static void AssertLayout()
        {
            int size = Marshal.SizeOf<HostConfig>();
            if (size != HostConfig.SizeBytes)
                throw new InvalidOperationException($"FinalScan ABI mismatch: FsHostConfig is {size} B, expected {HostConfig.SizeBytes} B");
        }
    }
}
