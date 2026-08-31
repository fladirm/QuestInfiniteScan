using System;
using System.Collections.Generic;
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
        private const string ViewerPackageName = "QuestMerkabaScan";

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private RoomScanner _scanner;

        public bool IsExporting { get; private set; }
        public string LastExportPath { get; private set; }
        public string LastStatus { get; private set; } = "Not exported";
        public string ExportPath => Path.Combine(Application.persistentDataPath,
            "MerkabaScan", "exports", ExportFileName);
        public string ViewerPackagePath => Path.Combine(
            Application.persistentDataPath, "MerkabaScan", "exports",
            ViewerPackageName);
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

                MerkabaExportMembraneResult membrane =
                    await BuildMembraneAsync(progress);

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

        public async Task<bool> ExportViewerPackageAsync()
        {
            if (IsExporting || _grid == null) return false;
            IsExporting = true;
            SetStatus("Exporting 3D Tiles…");
            string destination = ViewerPackagePath;
            string staging = destination + ".tmp";
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
                MerkabaExportMembraneResult membrane =
                    await BuildMembraneAsync(progress);
                MerkabaTilesetResult result = await Task.Run(() =>
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                    MerkabaTilesetResult written =
                        MerkabaTilesetWriter.WritePackage(staging, membrane,
                            progress);
                    PublishDirectory(staging, destination);
                    return written;
                });
                LastExportPath = Path.Combine(destination, "tileset.json");
                Logger.Info("Merkaba 3D Tiles metrics " +
                    $"canonical={membrane.CanonicalOccupiedCount} " +
                    $"measured={membrane.MeasuredPatchCount} " +
                    $"inferred={membrane.InferredPatchCount} " +
                    $"tiles={result.TileCount} vertices={result.VertexCount} " +
                    $"triangles={result.TriangleCount} bytes={result.ByteLength}");
                SetStatus($"3D Tiles: {result.TileCount} GLBs, " +
                    $"{result.TriangleCount} triangles");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba 3D Tiles export failed: " + exception);
                SetStatus("3D Tiles export failed: " + exception.Message);
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
                if (Directory.Exists(ViewerPackagePath))
                    Directory.Delete(ViewerPackagePath, true);
                if (Directory.Exists(ViewerPackagePath + ".tmp"))
                    Directory.Delete(ViewerPackagePath + ".tmp", true);
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

        private async Task<MerkabaExportMembraneResult> BuildMembraneAsync(
            IProgress<OperationWorkProgress> progress)
        {
            MerkabaTileAddress[] addresses = _grid.CaptureStoredTileIndex();
            var evidence = new Dictionary<Unity.Mathematics.int3, KernelState>();
            for (int offset = 0; offset < addresses.Length;
                 offset += MerkabaGrid.StreamBatchCapacity)
            {
                int count = Math.Min(MerkabaGrid.StreamBatchCapacity,
                    addresses.Length - offset);
                var batchAddresses = new MerkabaTileAddress[count];
                Array.Copy(addresses, offset, batchAddresses, 0, count);
                MerkabaTileSnapshot[] tiles = await _grid
                    .ReadStoredTilesAsync(batchAddresses);
                foreach (MerkabaTileSnapshot tile in tiles)
                for (int index = 0; index < tile.States.Length; index++)
                {
                    KernelState state = tile.States[index];
                    if (state.OccupancyEvidence == 0 && state.Flags == 0u &&
                        state.ColorConfidence == 0u)
                        continue;
                    Unity.Mathematics.int3 coord = MerkabaSpatial.Decode(
                        tile.Address.BlockCoord, tile.Address.LocalAddress,
                        index);
                    if (!evidence.TryAdd(coord, state))
                        throw new InvalidDataException(
                            $"Duplicate canonical export coordinate {coord}.");
                }
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.CapturingState,
                    Math.Min(offset + count, addresses.Length), addresses.Length,
                    $"Streamed {Math.Min(offset + count, addresses.Length)}/" +
                    $"{addresses.Length} canonical tiles"));
            }
            MerkabaExportShellResult shell = await Task.Run(() =>
                MerkabaExportShell.Build(evidence, progress));
            return await Task.Run(() =>
                MerkabaExportMembrane.Build(shell, progress));
        }

        private static void PublishDirectory(string staging, string destination)
        {
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string previous = destination + ".previous";
            if (Directory.Exists(previous)) Directory.Delete(previous, true);
            bool movedPrevious = false;
            try
            {
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, previous);
                    movedPrevious = true;
                }
                Directory.Move(staging, destination);
                if (movedPrevious) Directory.Delete(previous, true);
            }
            catch
            {
                if (!Directory.Exists(destination) && movedPrevious &&
                    Directory.Exists(previous))
                    Directory.Move(previous, destination);
                throw;
            }
        }

    }
}
