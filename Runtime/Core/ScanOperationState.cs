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
        FlushingTiles,
        CapturingState,
        ReadingFile,
        RebuildingStorageIndex,
        LocalizingAnchor,
        ApplyingState,
        BuildingExportEvidence,
        DilatingShell,
        HealingTinyHoles,
        ExtractingMerkabaShell,
        PreparingExportColors,
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

    /// <summary>Measured work reported by explicit SAVE/LOAD/EXPORT boundaries.</summary>
    internal readonly struct OperationWorkProgress
    {
        public readonly ScanOperationStage Stage;
        public readonly long Completed;
        public readonly long Total;
        public readonly string Text;

        public OperationWorkProgress(ScanOperationStage stage, long completed,
            long total, string text)
        {
            Stage = stage;
            Completed = completed;
            Total = total;
            Text = text;
        }

        public static OperationWorkProgress Indeterminate(
            ScanOperationStage stage, string text) => new(stage, 0L, -1L, text);
    }

    /// <summary>
    /// Maps real stage-local work to one monotonic operation bar. A final reserved
    /// unit ensures normal work can never display 100% before durable completion.
    /// </summary>
    internal sealed class ScanOperationProgressTracker
    {
        private ScanOperationKind _kind;
        private int _stageRank = -1;
        private float _stageFraction;
        private float _lastDeterminate;

        internal float LastDeterminate => _lastDeterminate;

        internal void Begin(ScanOperationKind kind)
        {
            _kind = kind;
            _stageRank = -1;
            _stageFraction = 0f;
            _lastDeterminate = 0f;
        }

        internal float Report(ScanOperationKind kind, ScanOperationStage stage,
            long completed, long total)
        {
            if (kind != _kind || total < 0L) return -1f;
            int rank = StageRank(kind, stage, out int stageCount);
            if (rank < 0 || stageCount <= 0) return _lastDeterminate;
            float fraction = total == 0L ? 1f : Mathf.Clamp01(
                completed / (float)Math.Max(1L, total));
            if (rank < _stageRank) return _lastDeterminate;
            if (rank == _stageRank)
                fraction = Mathf.Max(_stageFraction, fraction);
            else
            {
                _stageRank = rank;
                _stageFraction = 0f;
            }
            _stageFraction = fraction;
            _lastDeterminate = Mathf.Max(_lastDeterminate,
                (rank + fraction) / (stageCount + 1f));
            return _lastDeterminate;
        }

        private static int StageRank(ScanOperationKind kind,
            ScanOperationStage stage, out int count)
        {
            ScanOperationStage[] stages = kind switch
            {
                ScanOperationKind.Save => SaveStages,
                ScanOperationKind.Load => LoadStages,
                ScanOperationKind.ExportGlb => ExportStages,
                _ => Array.Empty<ScanOperationStage>()
            };
            count = stages.Length;
            return Array.IndexOf(stages, stage);
        }

        private static readonly ScanOperationStage[] SaveStages =
        {
            ScanOperationStage.SynchronizingScan,
            ScanOperationStage.FlushingTiles,
            ScanOperationStage.CapturingState,
            ScanOperationStage.WritingFile,
            ScanOperationStage.PublishingFile
        };

        private static readonly ScanOperationStage[] LoadStages =
        {
            ScanOperationStage.SynchronizingScan,
            ScanOperationStage.ReadingFile,
            ScanOperationStage.RebuildingStorageIndex,
            ScanOperationStage.LocalizingAnchor,
            ScanOperationStage.ApplyingState
        };

        private static readonly ScanOperationStage[] ExportStages =
        {
            ScanOperationStage.SynchronizingScan,
            ScanOperationStage.FlushingTiles,
            ScanOperationStage.CapturingState,
            ScanOperationStage.BuildingExportEvidence,
            ScanOperationStage.DilatingShell,
            ScanOperationStage.HealingTinyHoles,
            ScanOperationStage.ExtractingMerkabaShell,
            ScanOperationStage.PreparingExportColors,
            ScanOperationStage.BuildingMerkabaGeometry,
            ScanOperationStage.WritingFile,
            ScanOperationStage.PublishingFile
        };
    }
}
