using System;
using UnityEngine;

namespace FinalScan.Host
{
    /// <summary>
    /// Reads Android system properties (<c>adb shell setprop debug.finalscan.mode xray</c>) through
    /// android.os.SystemProperties. The class is a greylisted hidden API reachable via JNI; every failure returns the
    /// default so the host never depends on it. Callers poll at a coarse interval (2 s).
    /// </summary>
    public static class AndroidSystemProps
    {
        public const string ModeProp = "debug.finalscan.mode";
        public const string SpinTestProp = "debug.finalscan.spintest";
        /// <summary>Acceptance/test fixture only (C04/C07): value = synthetic kind + 1 (1 room box .. 5 dense); unset/0 = live path.</summary>
        public const string SyntheticProp = "debug.finalscan.synthetic";

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass s_class;
        static bool s_failed;

        public static string Get(string name, string fallback = "")
        {
            if (s_failed) return fallback;
            try
            {
                s_class ??= new AndroidJavaClass("android.os.SystemProperties");
                string v = s_class.CallStatic<string>("get", name, fallback);
                return v ?? fallback;
            }
            catch (Exception e)
            {
                s_failed = true;
                Debug.LogWarning("[FinalScan] SystemProperties unavailable: " + e.Message);
                return fallback;
            }
        }
#else
        public static string Get(string name, string fallback = "") => fallback;
#endif

        /// <summary>debug.finalscan.synthetic value → synthetic kind (value - 1), or -1 when unset, 0 or unparsable. Never creates a world in a normal run.</summary>
        public static int ParseSyntheticKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return -1;
            if (!int.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v)) return -1;
            return v >= 1 && v <= 5 ? v - 1 : -1;
        }

        /// <summary>"1", "true", "on", "yes" → true (case-insensitive); everything else false.</summary>
        public static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "1": case "true": case "on": case "yes": return true;
                default: return false;
            }
        }
    }
}
