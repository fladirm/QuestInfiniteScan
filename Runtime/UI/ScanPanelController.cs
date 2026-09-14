using System.Globalization;
using FinalScan.Host;
using FinalScan.Platform.Native;
using FinalScan.Render;
using FinalScan.Residency;
using UnityEngine;
using UnityEngine.UIElements;

namespace FinalScan.UI
{
    /// <summary>
    /// C22 scan panel (donor DebugMenuController port, minimised): world-space UI Toolkit document above the left
    /// controller. Content: status line (READY / WARMING / QUARANTINED …), START/STOP SCAN, MODE cycling
    /// (SCAN → XRAY → PLAN), SPIN TEST, RESET WORLD, and a DIAGNOSTICS toggle that reveals the live counters
    /// (hidden by default, FEATURE_PARITY contr K). Hidden until the left thumbstick click (InputMap) shows it.
    /// When the UIDocument has no PanelSettings (editor setup not run) the TextMesh HostHud stays as the fallback.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(ScanPanelFollower))]
    [DisallowMultipleComponent]
    public sealed class ScanPanelController : MonoBehaviour
    {
        public static ScanPanelController Current { get; private set; }

        [SerializeField] float refreshHz = 6f;
        [SerializeField] bool diagnosticsVisible;   // contr K: hidden by default

        UIDocument _document;
        ScanPanelFollower _follower;
        VisualElement _root, _boundRoot, _diagnostics;
        Button _scan, _mode, _spin, _reset, _diag;
        Label _status, _scanning, _guidance, _meas, _surfels, _pages, _visible, _generation, _frame, _headroom, _orient, _envDepth, _pointer, _spinVal, _resetVal;
        ControllerRayDriver _rayDriver;
        bool _visibleFlag;
        float _nextRefresh;
        float _smoothFrameMs = 13.9f;

        public bool IsVisible => _visibleFlag;
        public bool DiagnosticsVisible => diagnosticsVisible;
        public bool HasPanel => _document != null && _document.panelSettings != null;

        void Awake()
        {
            Current = this;
            _document = GetComponent<UIDocument>();
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) gameObject.layer = uiLayer;
            _document.sortingOrder = 1000;
            _follower = GetComponent<ScanPanelFollower>();
            // Fallback rule: TextMesh HUD only when the UI Toolkit panel cannot render.
            HostHud hud = FindAnyObjectByType<HostHud>(FindObjectsInactive.Include);
            if (hud != null) hud.enabled = !HasPanel;
            if (!HasPanel) Debug.LogWarning("[FinalScan] ScanPanel: UIDocument has no PanelSettings; HostHud fallback stays enabled.");
        }

        void OnEnable()
        {
            _root = _document.rootVisualElement;
            if (_root == null) return;
            _root.style.display = DisplayStyle.None;
            _visibleFlag = false;
            Query();
            if (_boundRoot != _root) { Bind(); _boundRoot = _root; }
            ApplyDiagnosticsVisibility();
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Update()
        {
            _smoothFrameMs += (Time.unscaledDeltaTime * 1000f - _smoothFrameMs) * 0.1f;
            if (!_visibleFlag || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, refreshHz);
            RefreshStatus();
        }

        public void Toggle() { if (_visibleFlag) Hide(); else Show(); }

        public void Show()
        {
            if (_root == null) return;
            _visibleFlag = true;
            _root.style.display = DisplayStyle.Flex;
            _follower?.SnapToLeftController();
            RefreshStatus();
        }

        public void Hide()
        {
            if (_root == null) return;
            _visibleFlag = false;
            _root.style.display = DisplayStyle.None;
            _follower?.StopTracking();
        }

        public void ToggleDiagnostics() { diagnosticsVisible = !diagnosticsVisible; ApplyDiagnosticsVisibility(); }

        void ApplyDiagnosticsVisibility()
        {
            if (_diagnostics == null) return;
            _diagnostics.EnableInClassList("mode-panel--hidden", !diagnosticsVisible);
            _diag?.EnableInClassList("quiet-action--selected", diagnosticsVisible);
        }

        void Query()
        {
            _scan = _root.Q<Button>("btn-scan");
            _mode = _root.Q<Button>("btn-mode");
            _spin = _root.Q<Button>("btn-spin");
            _reset = _root.Q<Button>("btn-reset");
            _diag = _root.Q<Button>("btn-diagnostics");
            _diagnostics = _root.Q<VisualElement>("diagnostics");
            _status = _root.Q<Label>("val-status");
            _scanning = _root.Q<Label>("val-scanning");
            _guidance = _root.Q<Label>("val-guidance");
            _meas = _root.Q<Label>("val-meas");
            _surfels = _root.Q<Label>("val-surfels");
            _pages = _root.Q<Label>("val-pages");
            _visible = _root.Q<Label>("val-visible");
            _generation = _root.Q<Label>("val-generation");
            _frame = _root.Q<Label>("val-frame");
            _headroom = _root.Q<Label>("val-headroom");
            _orient = _root.Q<Label>("val-orient");
            _envDepth = _root.Q<Label>("val-envdepth");
            _pointer = _root.Q<Label>("val-pointer");
            _spinVal = _root.Q<Label>("val-spin");
            _resetVal = _root.Q<Label>("val-reset");
        }

        void Bind()
        {
            _scan?.RegisterCallback<ClickEvent>(_ => { FinalScanHost.Current?.ToggleScanning(); RefreshStatus(); });
            _mode?.RegisterCallback<ClickEvent>(_ => { FinalScanHost.Current?.CycleMode(); RefreshStatus(); });
            _spin?.RegisterCallback<ClickEvent>(_ => { ResidencyDriver.Current?.RequestSpinTest(); RefreshStatus(); });
            _reset?.RegisterCallback<ClickEvent>(_ => { FinalScanHost.Current?.ResetWorld(); RefreshStatus(); });
            _diag?.RegisterCallback<ClickEvent>(_ => ToggleDiagnostics());
        }

        void RefreshStatus()
        {
            var ci = CultureInfo.InvariantCulture;
            FinalScanHost h = FinalScanHost.Current;
            if (h == null) { SetStatus(_status, "NO HOST", StatusKind.Error); return; }

            string status = h.NativeAvailable ? StatusText(h.Status) : "NATIVE ABSENT";
            StatusKind kind = !h.NativeAvailable ? StatusKind.Error : h.Status switch
            {
                FinalScanHostNative.HostStatus.Ready => StatusKind.Good,
                FinalScanHostNative.HostStatus.WarmingUp => StatusKind.Warning,
                FinalScanHostNative.HostStatus.Uninitialized => StatusKind.Neutral,
                _ => StatusKind.Error,
            };
            SetStatus(_status, status + (h.SyntheticWorldEnabled ? "  ·  SYNTHETIC " + h.SyntheticKind : "  ·  LIVE"), kind);

            // "scanning" is true only while measurements actually flow (FEATURE_PARITY §9.6), not merely requested.
            bool flowing = FinalScanHost.ScanEnabled && h.MeasurementsPerSec > 0.5;
            SetStatus(_scanning, !FinalScanHost.ScanEnabled ? "Stopped" : flowing ? "Scanning · " + h.MeasurementsPerSec.ToString("F0", ci) + " meas/s" : "Armed · waiting for measurements",
                      !FinalScanHost.ScanEnabled ? StatusKind.Neutral : flowing ? StatusKind.Good : StatusKind.Warning);
            if (_scan != null)
            {
                _scan.text = FinalScanHost.ScanEnabled ? "STOP SCAN" : "START SCAN";
                _scan.EnableInClassList("primary-action--stop", FinalScanHost.ScanEnabled);
                _scan.SetEnabled(h.NativeAvailable);
            }
            if (_mode != null) _mode.text = "MODE: " + RenderModeParser.Name(h.Mode).ToUpperInvariant();
            Set(_guidance, !h.NativeAvailable ? "Native plugin missing" : h.Status == FinalScanHostNative.HostStatus.WarmingUp ? "Warming up pipelines…" :
                h.Status == FinalScanHostNative.HostStatus.Ready && h.FrontSurfels == 0 ? "Look around to start building the world" : string.Empty);

            ResidencyDriver rd = ResidencyDriver.Current;
            if (_spin != null) _spin.SetEnabled(rd != null && !rd.SpinActive);
            if (!diagnosticsVisible) return;

            Set(_meas, h.MeasurementsPerSec.ToString("F0", ci) + "  (" + h.MeasurementsTotal + ")");
            Set(_surfels, h.FrontSurfels.ToString(ci));
            Set(_pages, h.ResidentPages + " / " + h.LogicalPages);
            Set(_visible, h.VisibleSurfels.ToString(ci) + "  rec " + h.DrawRecords);
            Set(_generation, h.FrontGeneration.ToString(ci));
            SetStatus(_frame, _smoothFrameMs.ToString("F1", ci) + (h.FrameTimingAvailable ? "  gpu " + h.LastGpuFrameMs.ToString("F1", ci) : ""),
                      _smoothFrameMs > 20f ? StatusKind.Error : _smoothFrameMs > 14.5f ? StatusKind.Warning : StatusKind.Good);
            SetStatus(_headroom, (h.LastGpuHeadroomUs / 1000f).ToString("F1", ci), h.LastGpuHeadroomUs < 0 ? StatusKind.Error : h.LastGpuHeadroomUs < 2000 ? StatusKind.Warning : StatusKind.Good);
            long orient = h.ZoneStats[4];
            SetStatus(_orient, orient.ToString(ci), orient == 0 ? StatusKind.Good : StatusKind.Error);
            Set(_envDepth, h.EnvDepth != null ? h.EnvDepth.Source + " · " + h.EnvDepth.Accepted : "--");
            _rayDriver ??= FindAnyObjectByType<ControllerRayDriver>();
            SetStatus(_pointer, _rayDriver == null ? "Missing" : _rayDriver.HasTrackedPose ? "Tracked · trigger selects" : "Waiting for controller pose",
                      _rayDriver == null ? StatusKind.Error : _rayDriver.HasTrackedPose ? StatusKind.Good : StatusKind.Warning);
            Set(_spinVal, rd == null ? "--" : rd.SpinActive ? rd.SpinElapsedSec.ToString("F0", ci) + " s · yaw " + rd.LastYawRateDegPerSec.ToString("F0", ci) + " deg/s" : rd.LastVerdict ?? "idle");
            Set(_resetVal, h.LastResetResult);
        }

        static string StatusText(FinalScanHostNative.HostStatus s) => s switch
        {
            FinalScanHostNative.HostStatus.Ready => "READY",
            FinalScanHostNative.HostStatus.WarmingUp => "WARMING",
            FinalScanHostNative.HostStatus.Quarantined => "QUARANTINED",
            FinalScanHostNative.HostStatus.DeviceLost => "DEVICE LOST",
            FinalScanHostNative.HostStatus.Shutdown => "SHUTDOWN",
            _ => "UNINITIALIZED",
        };

        static void Set(Label label, string text) { if (label != null) label.text = text ?? string.Empty; }

        static void SetStatus(Label label, string text, StatusKind kind)
        {
            if (label == null) return;
            label.text = text ?? string.Empty;
            label.EnableInClassList("status-val--good", kind == StatusKind.Good);
            label.EnableInClassList("status-val--warning", kind == StatusKind.Warning);
            label.EnableInClassList("status-val--error", kind == StatusKind.Error);
        }

        enum StatusKind { Neutral, Good, Warning, Error }
    }
}
