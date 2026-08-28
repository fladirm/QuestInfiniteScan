using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    public enum ScanOperationKind : byte
    {
        None,
        Save,
        Load,
        ExportGlb
    }

    public enum ScanOperationStage : byte
    {
        None,
        SynchronizingScan,
        CapturingState,
        ReadingFile,
        LocalizingAnchor,
        ApplyingState,
        BuildingExportEvidence,
        HealingTinyHoles,
        ExtractingMerkabaShell,
        BuildingMerkabaGeometry,
        WritingFile,
        PublishingFile,
        Complete,
        Failed
    }

    /// <summary>One generic, immutable UI readout for SAVE, LOAD, and EXPORT GLB.</summary>
    public readonly struct ScanOperationState
    {
        public readonly ScanOperationKind Kind;
        public readonly ScanOperationStage Stage;
        public readonly float Progress01;
        public readonly bool Busy;
        public readonly string StatusText;

        public ScanOperationState(ScanOperationKind kind, ScanOperationStage stage,
            float progress01, bool busy, string statusText)
        {
            Kind = kind;
            Stage = stage;
            Progress01 = progress01 < 0f ? -1f : Mathf.Clamp01(progress01);
            Busy = busy;
            StatusText = statusText ?? string.Empty;
        }

        public bool IsIndeterminate => Busy && Progress01 < 0f;
        public static ScanOperationState Idle => new(ScanOperationKind.None,
            ScanOperationStage.None, 0f, false, string.Empty);
    }

    internal readonly struct ExportShellProgress
    {
        public readonly ScanOperationStage Stage;
        public readonly float Progress01;
        public readonly string Text;

        public ExportShellProgress(ScanOperationStage stage, float progress01,
            string text)
        {
            Stage = stage;
            Progress01 = progress01;
            Text = text;
        }
    }
}
