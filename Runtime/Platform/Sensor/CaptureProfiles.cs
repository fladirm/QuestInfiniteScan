using System;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    /// <summary>Capture operating points (contract §3.2). Coarse states, never a per-frame cadence.</summary>
    public enum CaptureProfile
    {
        /// <summary>Default: idle / slow movement. 1280x960 @ 30.</summary>
        Normal30 = 0,
        /// <summary>Active scan, enough light, thermal OK. 1280x960 @ 60.</summary>
        Normal60 = 1,
        /// <summary>DETAIL request. 1280x1280 (wider FOV, v83+) @ 60. Provisional until C01 FOV/intrinsics evidence.</summary>
        Detail60 = 2,
        /// <summary>Sensor runs 50 Hz in low light so 60 cannot be guaranteed; request 30 (provisional; C01 decides 30 vs 50).</summary>
        LowLight = 3,
        /// <summary>Thermal warning or persistent frame misses. 1280x960 @ 30.</summary>
        Thermal30 = 4
    }

    /// <summary>Scan mode requested by the application layer (scheduler input, §3.2).</summary>
    public enum ScanMode { Idle = 0, Active = 1, Detail = 2 }

    public readonly struct CaptureProfileSpec
    {
        public readonly CaptureProfile Profile;
        public readonly Vector2Int Resolution;
        public readonly int MaxFramerate;
        public CaptureProfileSpec(CaptureProfile p, int w, int h, int fps) { Profile = p; Resolution = new Vector2Int(w, h); MaxFramerate = fps; }
        public double NominalPeriodNs => MaxFramerate > 0 ? 1e9 / MaxFramerate : 1e9 / 30.0;
    }

    public static class CaptureProfiles
    {
        /// <summary>Resolution/framerate per profile. 1280x960 is the crop the donors used; 1280x1280 is the 1:1 wide-FOV mode (v83+).</summary>
        public static CaptureProfileSpec Spec(CaptureProfile p)
        {
            switch (p)
            {
                case CaptureProfile.Normal60: return new CaptureProfileSpec(p, 1280, 960, 60);
                case CaptureProfile.Detail60: return new CaptureProfileSpec(p, 1280, 1280, 60);
                case CaptureProfile.LowLight: return new CaptureProfileSpec(p, 1280, 960, 30);
                case CaptureProfile.Thermal30: return new CaptureProfileSpec(p, 1280, 960, 30);
                default: return new CaptureProfileSpec(CaptureProfile.Normal30, 1280, 960, 30);
            }
        }

        public static Vector2Int Resolution(CaptureProfile p) => Spec(p).Resolution;
        public static int MaxFramerate(CaptureProfile p) => Spec(p).MaxFramerate;

        /// <summary>Profile the scan mode asks for when nothing degrades it.</summary>
        public static CaptureProfile Nominal(ScanMode mode)
        {
            switch (mode)
            {
                case ScanMode.Active: return CaptureProfile.Normal60;
                case ScanMode.Detail: return CaptureProfile.Detail60;
                default: return CaptureProfile.Normal30;
            }
        }

        public static bool IsFallback(CaptureProfile p) => p == CaptureProfile.LowLight || p == CaptureProfile.Thermal30;
    }

    /// <summary>
    /// Coarse capture-profile state machine with hysteresis (contract §3.2). Pure C#: the caller feeds wall-clock
    /// seconds, requested scan mode, measured delivered fps per eye and the thermal flag; the governor emits at most one
    /// profile transition per dwell period (min 3 s). It never reconfigures the camera itself: <see cref="Update"/>
    /// returns true when the caller must apply <see cref="Current"/> (disable both PCA, set resolution/framerate, enable).
    ///
    /// Rules:
    ///  * thermal warning → Thermal30 (held at least <see cref="FallbackHoldSeconds"/> after the warning clears);
    ///  * delivered fps (min of L/R) &lt; 0.8 × requested for <see cref="UnderdeliverySeconds"/> continuous → LowLight
    ///    (Thermal30 when the thermal flag is also set); held for a hold time that doubles on every repeated fallback
    ///    (10 s → 20 s → 40 s → 60 s cap) so a dim room does not oscillate 60↔30; the backoff resets after
    ///    <see cref="HealthySecondsToResetBackoff"/> of healthy delivery at a nominal profile;
    ///  * otherwise the nominal profile of the scan mode, changed only after the dwell has elapsed.
    /// </summary>
    public sealed class ProfileGovernor
    {
        public const double MinDwellSeconds = 3.0;
        public const double UnderdeliverySeconds = 3.0;
        public const double UnderdeliveryRatio = 0.8;
        public const double FallbackHoldSeconds = 10.0;
        public const double FallbackHoldMaxSeconds = 60.0;
        public const double HealthySecondsToResetBackoff = 30.0;

        public CaptureProfile Current { get; private set; }
        public ScanMode Mode { get; private set; }
        public bool ThermalWarning { get; private set; }
        public double LastTransitionSeconds { get; private set; } = double.NegativeInfinity;
        public int Transitions { get; private set; }
        public int Fallbacks { get; private set; }
        /// <summary>Seconds of continuous under-delivery observed at the current profile.</summary>
        public double UnderdeliveryElapsed { get; private set; }
        public double CurrentHoldSeconds { get; private set; } = FallbackHoldSeconds;
        public string LastReason { get; private set; } = "init";

        double _fallbackUntil = double.NegativeInfinity;
        double _underdeliveryStart = double.NaN;
        double _healthySince = double.NaN;
        bool _started;

        public ProfileGovernor(CaptureProfile initial = CaptureProfile.Normal30) { Current = initial; }

        public void SetMode(ScanMode mode) => Mode = mode;
        public void SetThermalWarning(bool warning) => ThermalWarning = warning;

        /// <summary>Convenience overload; see <see cref="Update(double,ScanMode,double,double,bool)"/>.</summary>
        public bool Update(double nowSeconds, double deliveredFpsL, double deliveredFpsR)
            => Update(nowSeconds, Mode, deliveredFpsL, deliveredFpsR, ThermalWarning);

        /// <summary>
        /// Advances the state machine. Delivered fps values ≤ 0 mean "not measured yet" (never counted as under-delivery).
        /// Returns true when <see cref="Current"/> changed and must be applied.
        /// </summary>
        public bool Update(double nowSeconds, ScanMode mode, double deliveredFpsL, double deliveredFpsR, bool thermalWarning)
        {
            Mode = mode; ThermalWarning = thermalWarning;
            if (!_started) { _started = true; LastTransitionSeconds = nowSeconds; }

            int requested = CaptureProfiles.MaxFramerate(Current);
            double delivered = Math.Min(deliveredFpsL <= 0 ? double.PositiveInfinity : deliveredFpsL, deliveredFpsR <= 0 ? double.PositiveInfinity : deliveredFpsR);
            bool measured = !double.IsPositiveInfinity(delivered);
            bool under = measured && delivered < UnderdeliveryRatio * requested;
            if (under) { if (double.IsNaN(_underdeliveryStart)) _underdeliveryStart = nowSeconds; UnderdeliveryElapsed = nowSeconds - _underdeliveryStart; }
            else { _underdeliveryStart = double.NaN; UnderdeliveryElapsed = 0; }

            bool healthy = measured && !under && !thermalWarning;
            if (healthy && !CaptureProfiles.IsFallback(Current)) { if (double.IsNaN(_healthySince)) _healthySince = nowSeconds; }
            else _healthySince = double.NaN;
            if (!double.IsNaN(_healthySince) && nowSeconds - _healthySince >= HealthySecondsToResetBackoff) CurrentHoldSeconds = FallbackHoldSeconds;

            bool dwellOk = nowSeconds - LastTransitionSeconds >= MinDwellSeconds;
            CaptureProfile nominal = CaptureProfiles.Nominal(mode);
            CaptureProfile target = Current; string reason = null;

            if (thermalWarning)
            {
                target = CaptureProfile.Thermal30; reason = "thermal";
                _fallbackUntil = Math.Max(_fallbackUntil, nowSeconds + CurrentHoldSeconds);
            }
            else if (under && UnderdeliveryElapsed >= UnderdeliverySeconds && !CaptureProfiles.IsFallback(Current))
            {
                target = CaptureProfile.LowLight; reason = "underdelivery " + delivered.ToString("F1") + "<" + (UnderdeliveryRatio * requested).ToString("F1");
                _fallbackUntil = nowSeconds + CurrentHoldSeconds;
                CurrentHoldSeconds = Math.Min(CurrentHoldSeconds * 2, FallbackHoldMaxSeconds);
            }
            else if (CaptureProfiles.IsFallback(Current))
            {
                if (nowSeconds >= _fallbackUntil && dwellOk) { target = nominal; reason = "hold expired -> " + nominal; }
            }
            else if (nominal != Current)
            {
                if (dwellOk) { target = nominal; reason = "mode " + mode; }
            }

            if (target == Current) return false;
            if (!dwellOk && !(thermalWarning && target == CaptureProfile.Thermal30 && Current != CaptureProfile.Thermal30 && nowSeconds - LastTransitionSeconds >= MinDwellSeconds))
            {
                // Dwell not satisfied: thermal is the only input allowed to override it, and even that respects the dwell.
                return false;
            }
            if (CaptureProfiles.IsFallback(target)) Fallbacks++;
            Current = target; LastTransitionSeconds = nowSeconds; Transitions++; LastReason = reason ?? "";
            _underdeliveryStart = double.NaN; UnderdeliveryElapsed = 0; _healthySince = double.NaN;
            return true;
        }
    }
}
