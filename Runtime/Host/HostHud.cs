using System.Globalization;
using System.Text;
using FinalScan.Platform.Native;
using FinalScan.Render;
using FinalScan.Residency;
using UnityEngine;
using RenderMode = FinalScan.Render.RenderMode;

namespace FinalScan.Host
{
    /// <summary>
    /// World-space TextMesh HUD (C02..C07 device readout): host status, pages/surfels, visible surfels, per-class
    /// scheduler stats, frame/GPU ms, zone stats and render mode. Follows the main camera at a fixed distance;
    /// refreshed 4x per second so the HUD never becomes measurable work (§20 "telemetry injecting work").
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HostHud : MonoBehaviour
    {
        [SerializeField] float distanceM = 1.6f;
        [SerializeField] float heightOffsetM = -0.15f;
        [SerializeField] float characterSize = 0.012f;
        [SerializeField] int fontSize = 48;
        [SerializeField] float refreshHz = 4f;
        [SerializeField] bool follow = true;

        static readonly string[] ClassNames = { "PUB", "SCAN", "RES", "APP", "COLD" };
        TextMesh _text;
        Transform _anchor;
        float _nextRefresh;
        readonly StringBuilder _sb = new StringBuilder(1024);
        readonly long[] _cls = new long[FinalScanHostNative.ClassStatsCount];
        float _smoothFrameMs = 13.9f;

        public string Text => _text != null ? _text.text : string.Empty;

        void Awake()
        {
            var go = new GameObject("[FinalScan HUD]");
            go.transform.SetParent(transform, false);
            _anchor = go.transform;
            _text = go.AddComponent<TextMesh>();
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (System.Exception) { }
            if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (System.Exception) { } }
            if (font != null)
            {
                _text.font = font;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = font.material;
            }
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Left;
            _text.characterSize = characterSize;
            _text.fontSize = fontSize;
            _text.color = new Color(0.85f, 1f, 0.85f, 0.95f);
            _text.richText = false;
            _text.text = "FinalScan";
        }

        void LateUpdate()
        {
            _smoothFrameMs += (Time.unscaledDeltaTime * 1000f - _smoothFrameMs) * 0.1f;
            Camera cam = Camera.main;
            if (follow && cam != null)
            {
                Transform t = cam.transform;
                Vector3 fwd = t.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                _anchor.position = t.position + fwd * distanceM + Vector3.up * heightOffsetM;
                _anchor.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f / Mathf.Max(0.5f, refreshHz);
            _text.text = Compose();
        }

        string Compose()
        {
            var ci = CultureInfo.InvariantCulture;
            FinalScanHost h = FinalScanHost.Current;
            SurfelRenderer r = SurfelRenderer.Current;
            ResidencyDriver rd = ResidencyDriver.Current;
            _sb.Clear();
            _sb.Append("FinalScan ").Append(FinalScanBootstrap.Version).Append('\n');
            if (h == null) { _sb.Append("no host"); return _sb.ToString(); }
            _sb.Append("host: ").Append(h.NativeAvailable ? h.Status.ToString() : "native absent (abi " + h.ReportedAbi + ")")
               .Append("  mode: ").Append(RenderModeParser.Name(h.Mode)).Append(h.SyntheticWorldEnabled ? "  [SYNTHETIC " + h.SyntheticKind + "]" : "  [LIVE]").Append('\n');
            _sb.Append("meas/s ").Append(h.MeasurementsPerSec.ToString("F0", ci)).Append("  surfels ").Append(h.FrontSurfels)
               .Append("  pages ").Append(h.ResidentPages).Append("  visible ").Append(h.VisibleSurfels)
               .Append("  gen ").Append(h.FrontGeneration).Append('\n');
            _sb.Append("frame ").Append(_smoothFrameMs.ToString("F1", ci)).Append(" ms  gpu ")
               .Append(h.FrameTimingAvailable ? h.LastGpuFrameMs.ToString("F1", ci) : "n/a").Append(" ms  headroom ")
               .Append((h.LastGpuHeadroomUs / 1000f).ToString("F1", ci)).Append(" ms\n");
            _sb.Append("pages ").Append(h.ResidentPages).Append('/').Append(h.LogicalPages)
               .Append("  surfels F ").Append(h.FrontSurfels).Append(" B ").Append(h.BackSurfels)
               .Append("  gen ").Append(h.FrontGeneration).Append('\n');
            _sb.Append("visible ").Append(h.VisibleSurfels).Append("  records ").Append(h.DrawRecords)
               .Append("  cull ").Append(h.CullGpuUs).Append(" us  culledPages ").Append(h.CulledPages).Append('\n');
            long[] z = h.ZoneStats;
            _sb.Append("zones I/W/P ").Append(z[0]).Append('/').Append(z[1]).Append('/').Append(z[2])
               .Append("  req ").Append(z[3]).Append("  orient ").Append(z[4]).Append("  evict ").Append(z[5])
               .Append("  loads ").Append(z[6]).Append("  stalls ").Append(z[7]).Append('\n');
            if (r != null)
                _sb.Append("draw ").Append(r.Registered ? "registered" : "unregistered(" + r.RegisterAttempts + ")")
                   .Append("  draws ").Append(r.DrawsIssued).Append("  budget ").Append(r.ScreenWorkBudget).Append('\n');
            for (int i = 0; i < FinalScanHostNative.JobClassCount; i++)
            {
                if (!h.GetClassStats((FinalScanHostNative.JobClass)i, _cls)) continue;
                _sb.Append(ClassNames[i]).Append(" sub ").Append(_cls[0]).Append(" ret ").Append(_cls[1]).Append(" def ").Append(_cls[2])
                   .Append(" fail ").Append(_cls[3]).Append(" gpu ").Append(_cls[5]).Append("us inflight ").Append(_cls[6]).Append('\n');
            }
            if (h.EnvDepth != null) _sb.Append("envDepth ").Append(h.EnvDepth.Source).Append(' ').Append(h.EnvDepth.Accepted).Append(" frames rc ").Append(h.EnvDepth.LastResult).Append('/').Append(h.EnvDepth.LastMeasResult).Append('\n');
            if (rd != null && rd.SpinActive) _sb.Append("SPIN ").Append(rd.SpinElapsedSec.ToString("F0", ci)).Append(" s  yaw ").Append(rd.LastYawRateDegPerSec.ToString("F0", ci)).Append(" deg/s\n");
            else if (rd != null && rd.LastVerdict != null) _sb.Append("spin verdict: ").Append(rd.LastVerdict).Append('\n');
            return _sb.ToString();
        }
    }
}
