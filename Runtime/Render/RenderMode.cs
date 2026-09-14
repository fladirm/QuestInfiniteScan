using System;

namespace FinalScan.Render
{
    /// <summary>Contract §13.5 render modes. Switching a mode changes only cull policy and shader, never residency or canonical data.</summary>
    public enum RenderMode { Scan = 0, XRay = 1, Plan = 2 }

    public static class RenderModeParser
    {
        /// <summary>
        /// Parses the value of <c>setprop debug.finalscan.mode</c> (or the serialized enum name). Accepts scan|xray|x-ray|radar|plan
        /// and the numeric ids 0..2, case-insensitive; returns false for empty/unknown text so the caller keeps the current mode.
        /// </summary>
        public static bool TryParse(string text, out RenderMode mode)
        {
            mode = RenderMode.Scan;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().ToLowerInvariant();
            switch (t)
            {
                case "scan": case "live": case "0": mode = RenderMode.Scan; return true;
                case "xray": case "x-ray": case "radar": case "1": mode = RenderMode.XRay; return true;
                case "plan": case "2": mode = RenderMode.Plan; return true;
                default: return false;
            }
        }

        public static string Name(RenderMode mode)
        {
            switch (mode)
            {
                case RenderMode.XRay: return "xray";
                case RenderMode.Plan: return "plan";
                default: return "scan";
            }
        }
    }
}
