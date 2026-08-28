using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan
{
    public enum ScanAction
    {
        None,
        ToggleDebugMenu,
        ToggleScanning,
        Save,
        Load,
        NewClear,
        ExportGlb
    }

    [Serializable]
    public sealed class ScanInputBinding
    {
        public ScanAction action = ScanAction.None;
        public OVRInput.Button button = OVRInput.Button.None;
        public bool enabled = true;
    }

    /// <summary>Quest input plumbing; the default gesture only opens the donor-style menu.</summary>
    public sealed class RoomScanInputHandler : MonoBehaviour
    {
        [SerializeField] private List<ScanInputBinding> bindings = new()
        {
            new()
            {
                action = ScanAction.ToggleDebugMenu,
                button = OVRInput.Button.PrimaryThumbstick,
                enabled = true
            }
        };

        public List<ScanInputBinding> Bindings => bindings;

        public void AddBinding(ScanAction action, OVRInput.Button button) =>
            bindings.Add(new ScanInputBinding { action = action, button = button });
        public void RemoveBindingsForAction(ScanAction action) =>
            bindings.RemoveAll(binding => binding.action == action);
        public void RemoveBindingsForButton(OVRInput.Button button) =>
            bindings.RemoveAll(binding => binding.button == button);
        public void ClearAllBindings() => bindings.Clear();

        private void Update()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            foreach (ScanInputBinding binding in bindings)
            {
                if (!binding.enabled || binding.action == ScanAction.None ||
                    binding.button == OVRInput.Button.None ||
                    !OVRInput.GetDown(binding.button))
                    continue;
                Execute(scanner, binding.action);
            }
        }

        private static void Execute(RoomScanner scanner, ScanAction action)
        {
            switch (action)
            {
                case ScanAction.ToggleDebugMenu: scanner.ToggleDebugMenu(); break;
                case ScanAction.ToggleScanning: scanner.ToggleScanning(); break;
                case ScanAction.Save: _ = scanner.SaveAsync(); break;
                case ScanAction.Load: _ = scanner.LoadAsync(); break;
                case ScanAction.NewClear: _ = scanner.NewClearAsync(); break;
                case ScanAction.ExportGlb: _ = scanner.ExportGlbAsync(); break;
            }
        }
    }
}
