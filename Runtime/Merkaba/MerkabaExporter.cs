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
            IsExporting = true;
            SetStatus("Exporting GLB…");
            try
            {
                if (_integrator != null && _integrator.HasPendingObservation)
                    throw new InvalidOperationException(
                        "Export requires RoomScanner quiesce before readout.");
                IProgress<OperationWorkProgress> progress =
                    new Progress<OperationWorkProgress>(value =>
                        _scanner?.ReportOperation(
                            ScanOperationKind.ExportGlb, value));
                await _grid.FlushAllDirtyTilesAsync(progress);

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
                        _integrator != null ? _integrator.IntegrationCount : 0,
                        progress);
                MerkabaExportShellResult shell = await Task.Run(() =>
                    MerkabaExportShell.Build(snapshot, progress));
                MerkabaExportMembraneResult membrane = await Task.Run(() =>
                    MerkabaExportMembrane.Build(shell, progress));

                string destination = ExportPath;
                string directory = Path.GetDirectoryName(destination);
                string temporary = destination + ".tmp";
                MerkabaGlbResult result = await Task.Run(() =>
                {
                    Directory.CreateDirectory(directory);
                    using (var stream = new FileStream(temporary, FileMode.Create,
                               FileAccess.Write, FileShare.None, 1024 * 1024,
                               FileOptions.WriteThrough))
                    {
                        MerkabaGlbResult written = MerkabaGlbWriter.Write(stream,
                            membrane, progress);
                        stream.Flush(true);
                        return written;
                    }
                });

                progress.Report(new OperationWorkProgress(
                    ScanOperationStage.PublishingFile, 0, 1,
                    "Publishing durable GLB"));
                await Task.Run(() => MerkabaFilePublishing.Publish(temporary,
                    destination));
                progress.Report(new OperationWorkProgress(
                    ScanOperationStage.PublishingFile, 1, 1,
                    "GLB published"));
                LastExportPath = destination;
                Logger.Info("Merkaba GLB metrics " +
                            $"canonical={membrane.CanonicalOccupiedCount} " +
                            $"measuredPlane={membrane.MeasuredPlaneOccupiedCount} " +
                            $"membraneMeasured={membrane.MeasuredPatchCount} " +
                            $"inferredGray={membrane.InferredPatchCount} " +
                            $"legacy={membrane.LegacyMeasuredUnknownPlaneCount} " +
                            $"removed=0 vertices={result.VertexCount} " +
                            $"triangles={result.PrimitiveCount} bytes={result.ByteLength}");
                SetStatus($"GLB: {result.PrimitiveCount} triangles, " +
                          $"{membrane.MeasuredPatchCount} measured, " +
                          $"{membrane.InferredPatchCount} gray inferred, " +
                          $"{membrane.LegacyMeasuredUnknownPlaneCount} legacy planes");
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

    }
}
