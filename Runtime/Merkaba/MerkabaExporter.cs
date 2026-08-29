using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>On-demand offline GLB PBR readout of the canonical Merkaba grid.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaExporter : MonoBehaviour
    {
        private const string ExportFileName = "QuestMerkabaScan.glb";

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private RoomScanner _scanner;

        public bool IsExporting { get; private set; }
        public string LastExportPath { get; private set; }
        public string LastStatus { get; private set; } = "Not exported";
        public string ExportPath => Path.Combine(Application.persistentDataPath,
            "MerkabaScan", "exports", ExportFileName);
        public event Action StatusChanged;

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            _scanner = GetComponent<RoomScanner>();
        }

        public async Task<bool> ExportGlbAsync()
        {
            if (IsExporting || _grid == null) return false;
            if (_scanner != null && !_scanner.TryBeginOperation(
                    ScanOperationKind.ExportGlb,
                    ScanOperationStage.SynchronizingScan,
                    "Synchronizing scan"))
                return false;
            IsExporting = true;
            SetStatus("Exporting GLB…");
            bool succeeded = false;
            try
            {
                Report(ScanOperationStage.SynchronizingScan, -1f,
                    "Scan quiesced");
                if (_integrator != null && _integrator.HasPendingObservation)
                    throw new InvalidOperationException(
                        "Export requires RoomScanner quiesce before readout.");
                await _grid.FlushAllDirtyTilesAsync();

                Report(ScanOperationStage.CapturingState, 0.08f,
                    "Capturing canonical evidence");
                Guid anchorUuid = Guid.Empty;
                Matrix4x4 anchorAtSave = Matrix4x4.identity;
                RoomAnchorManager anchor = RoomAnchorManager.Instance;
                if (anchor != null && anchor.HasSpatialAnchor)
                {
                    anchorUuid = anchor.SpatialAnchorUuid;
                    anchorAtSave = anchor.SpatialAnchorMatrix;
                }
                MerkabaSessionSnapshot snapshot = await _grid
                    .CaptureStoredSnapshotAsync(anchorUuid, anchorAtSave,
                        _integrator != null ? _integrator.IntegrationCount : 0);
                IProgress<ExportShellProgress> shellProgress =
                    new Progress<ExportShellProgress>(value =>
                    Report(value.Stage, value.Progress01, value.Text));
                MerkabaExportShellResult shell = await Task.Run(() =>
                    MerkabaExportShell.Build(snapshot, shellProgress));

                string destination = ExportPath;
                string directory = Path.GetDirectoryName(destination);
                string temporary = destination + ".tmp";
                Report(ScanOperationStage.BuildingMerkabaGeometry, 0.72f,
                    "Building canonical Merkaba geometry");
                MerkabaGlbResult result = await Task.Run(() =>
                {
                    Directory.CreateDirectory(directory);
                    using (var stream = new FileStream(temporary, FileMode.Create,
                               FileAccess.Write, FileShare.None, 1024 * 1024,
                               FileOptions.WriteThrough))
                    {
                        MerkabaGlbResult written = MerkabaGlbWriter.Write(stream,
                            shell.Kernels, () => shellProgress.Report(
                                new ExportShellProgress(
                                    ScanOperationStage.WritingFile, 0.84f,
                                    "Writing GLB")));
                        stream.Flush(true);
                        return written;
                    }
                });

                Report(ScanOperationStage.PublishingFile, 0.95f,
                    "Publishing GLB file");
                await Task.Run(() => MerkabaFilePublishing.Publish(temporary,
                    destination));
                LastExportPath = destination;
                SetStatus($"GLB: {result.PrimitiveCount} triangles, " +
                          $"{shell.ShellCoordinates.Length} shell kernels, " +
                          $"{shell.SyntheticKernelCount} healed");
                succeeded = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba GLB export failed: " + exception);
                SetStatus("Export failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsExporting = false;
                _scanner?.FinishOperation(ScanOperationKind.ExportGlb,
                    succeeded, LastStatus);
                StatusChanged?.Invoke();
            }
        }

        public void ClearExport()
        {
            if (IsExporting) return;
            try
            {
                if (File.Exists(ExportPath)) File.Delete(ExportPath);
                if (File.Exists(ExportPath + ".tmp")) File.Delete(ExportPath + ".tmp");
                LastExportPath = null;
                SetStatus("Not exported");
            }
            catch (Exception exception)
            {
                Logger.Error("Could not clear Merkaba GLB export: " + exception.Message);
                SetStatus("Export clear failed: " + exception.Message);
            }
        }

        private void SetStatus(string status)
        {
            LastStatus = status;
            StatusChanged?.Invoke();
        }

        private void Report(ScanOperationStage stage, float progress01,
            string text) => _scanner?.ReportOperation(ScanOperationKind.ExportGlb,
            stage, progress01, text);
    }
}
