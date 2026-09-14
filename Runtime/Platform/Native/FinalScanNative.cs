using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FinalScan.Platform.Native
{
    /// <summary>P/Invoke surface of libFinalScanNative.so. Mirrors Native/include/finalscan_native_api.h (ABI 1).</summary>
    public static class FinalScanNative
    {
        public const int AbiVersion = 1;
        const string Lib = "FinalScanNative";

        public enum RenderEvent { ProbeEmptySubmit = 1, ProbeTimestamp = 2, ProbeOverlapStart = 3, ProbeOverlapStop = 4, ProbePipelineBinary = 5, ProbeFrameMark = 6 }

#if UNITY_ANDROID && !UNITY_EDITOR
        [DllImport(Lib)] static extern int FsNative_GetAbiVersion();
        [DllImport(Lib)] static extern int FsNative_IsVulkanReady();
        [DllImport(Lib)] static extern int FsNative_GetDeviceReportJson(byte[] buf, int cap);
        [DllImport(Lib)] static extern int FsNative_GetProbeResultsJson(byte[] buf, int cap);
        [DllImport(Lib)] static extern IntPtr FsNative_GetRenderEventFunc();
        [DllImport(Lib)] static extern long FsNative_MonotonicNowNs();
        [DllImport(Lib)] static extern long FsNative_BoottimeNowNs();
        [DllImport(Lib)] static extern int FsNative_CameraProbeStart(int w, int h, int fps);
        [DllImport(Lib)] static extern int FsNative_CameraProbeStop();
        [DllImport(Lib)] static extern int FsNative_CameraProbeReportJson(byte[] buf, int cap);
        [DllImport(Lib)] static extern int FsNative_AhbImportProbe();
        public static bool Available { get { try { return FsNative_GetAbiVersion() == AbiVersion; } catch (DllNotFoundException) { return false; } catch (EntryPointNotFoundException) { return false; } } }
        public static bool VulkanReady => Available && FsNative_IsVulkanReady() != 0;
        public static string DeviceReportJson() => ReadJson(FsNative_GetDeviceReportJson);
        public static string ProbeResultsJson() => ReadJson(FsNative_GetProbeResultsJson);
        public static IntPtr RenderEventFunc => FsNative_GetRenderEventFunc();
        public static long MonotonicNowNs() => FsNative_MonotonicNowNs();
        public static long BoottimeNowNs() => FsNative_BoottimeNowNs();
        public static int CameraProbeStart(int w, int h, int fps) => FsNative_CameraProbeStart(w, h, fps);
        public static int CameraProbeStop() => FsNative_CameraProbeStop();
        public static string CameraProbeReportJson() => ReadJson(FsNative_CameraProbeReportJson);
        public static int AhbImportProbe() => FsNative_AhbImportProbe();
#else
        public static bool Available => false;
        public static bool VulkanReady => false;
        public static string DeviceReportJson() => "{}";
        public static string ProbeResultsJson() => "{}";
        public static IntPtr RenderEventFunc => IntPtr.Zero;
        public static long MonotonicNowNs() => DateTime.UtcNow.Ticks * 100;
        public static long BoottimeNowNs() => DateTime.UtcNow.Ticks * 100;
        public static int CameraProbeStart(int w, int h, int fps) => 1;
        public static int CameraProbeStop() => 1;
        public static string CameraProbeReportJson() => "{}";
        public static int AhbImportProbe() => 1;
#endif
        delegate int JsonGetter(byte[] buf, int cap);
        static string ReadJson(JsonGetter getter)
        {
            var buf = new byte[16384];
            int need = getter(buf, buf.Length);
            if (need > buf.Length) { buf = new byte[need + 1]; need = getter(buf, buf.Length); }
            int len = Array.IndexOf(buf, (byte)0); if (len < 0) len = Math.Min(need, buf.Length);
            return Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
