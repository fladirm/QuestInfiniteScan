using System;
using System.Collections.Generic;
using FinalScan.Host;
using UnityEngine;

namespace FinalScan.UI
{
    public enum ScanAction { None, TogglePanel, ToggleScanning, CycleMode, SpinTest, ToggleDiagnostics }

    [Serializable]
    public sealed class ScanInputBinding
    {
        public ScanAction action = ScanAction.None;
        public OVRInput.Button button = OVRInput.Button.None;
        public bool enabled = true;
    }

    /// <summary>
    /// Quest input plumbing (donor RoomScanInputHandler port). Exact mapping:
    ///   left thumbstick click  (OVRInput.Button.PrimaryThumbstick)   → toggle the scan panel
    ///   right index trigger    (OVRInput.Button.SecondaryIndexTrigger) → UI click/select through OVRInputModule while the
    ///                                                                   ray hovers the panel; scene tools only see it
    ///                                                                   when ControllerRayDriver.IsPointingAtUi is false
    ///   right hand trigger (grip) reserved for ERASE (C23), nothing bound here.
    /// Bindings are serialized so a build can remap without code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputMap : MonoBehaviour
    {
        [SerializeField] List<ScanInputBinding> bindings = new List<ScanInputBinding>
        {
            new ScanInputBinding { action = ScanAction.TogglePanel, button = OVRInput.Button.PrimaryThumbstick, enabled = true },
        };

        ControllerRayDriver _rayDriver;
        ScanPanelController _panel;

        public List<ScanInputBinding> Bindings => bindings;
        /// <summary>Right index trigger held and NOT consumed by the UI (future scene tools).</summary>
        public bool SceneTriggerHeld { get; private set; }

        void Update()
        {
            _rayDriver ??= FindAnyObjectByType<ControllerRayDriver>();
            _panel ??= FindAnyObjectByType<ScanPanelController>(FindObjectsInactive.Include);
            bool uiOwnsTrigger = _rayDriver != null && _rayDriver.IsPointingAtUi;
            bool trigger;
            try { trigger = OVRInput.Get(OVRInput.RawButton.RIndexTrigger); } catch (Exception) { trigger = false; }
            SceneTriggerHeld = !uiOwnsTrigger && trigger;
            foreach (ScanInputBinding b in bindings)
            {
                if (!b.enabled || b.action == ScanAction.None || b.button == OVRInput.Button.None) continue;
                bool down;
                try { down = OVRInput.GetDown(b.button); } catch (Exception) { down = false; }
                if (down) Execute(b.action);
            }
        }

        public void Execute(ScanAction action)
        {
            switch (action)
            {
                case ScanAction.TogglePanel: _panel?.Toggle(); break;
                case ScanAction.ToggleScanning: FinalScanHost.Current?.ToggleScanning(); break;
                case ScanAction.CycleMode: FinalScanHost.Current?.CycleMode(); break;
                case ScanAction.SpinTest: Residency.ResidencyDriver.Current?.RequestSpinTest(); break;
                case ScanAction.ToggleDiagnostics: _panel?.ToggleDiagnostics(); break;
            }
        }
    }
}
