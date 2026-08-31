using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        private const string ViewerArchiveFileName = "QuestMerkabaScan.zip";
        private const string ViewerResourceRoot =
            "Merkaba/QuestMerkabaScanViewer";

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
            ViewerArchiveFileName);
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
                               FileOptions.SequentialScan))
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
                            $"removed={membrane.RemovedBehindMembraneCount} " +
                            $"vertices={result.VertexCount} " +
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
            string exportDirectory = Path.GetDirectoryName(destination);
            string staging = Path.Combine(exportDirectory,
                ViewerPackageName + ".tmp");
            string temporaryArchive = destination + ".tmp";
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
                byte[] viewerHtml = LoadViewerResource(ViewerResourceRoot);
                byte[] threeLicense = LoadViewerResource(
                    ViewerResourceRoot + "ThreeLicense");
                byte[] tilesLicense = LoadViewerResource(
                    ViewerResourceRoot + "TilesLicense");
                var output = await Task.Run(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(exportDirectory);
                        if (Directory.Exists(staging))
                            Directory.Delete(staging, true);
                        if (File.Exists(temporaryArchive))
                            File.Delete(temporaryArchive);
                        MerkabaTilesetResult written =
                            MerkabaTilesetWriter.WritePackage(staging,
                                membrane, progress);
                        File.WriteAllBytes(Path.Combine(staging, "index.html"),
                            viewerHtml);
                        File.WriteAllBytes(Path.Combine(staging,
                                "THIRD_PARTY_THREE_LICENSE.txt"),
                            threeLicense);
                        File.WriteAllBytes(Path.Combine(staging,
                                "THIRD_PARTY_3DTILESRENDERERJS_LICENSE.txt"),
                            tilesLicense);
                        long archiveBytes = WriteViewerArchive(staging,
                            temporaryArchive);
                        MerkabaFilePublishing.Publish(temporaryArchive,
                            destination);
                        return (Written: written, ArchiveBytes: archiveBytes);
                    }
                    finally
                    {
                        if (Directory.Exists(staging))
                            Directory.Delete(staging, true);
                        if (File.Exists(temporaryArchive))
                            File.Delete(temporaryArchive);
                    }
                });
                MerkabaTilesetResult result = output.Written;
                LastExportPath = destination;
                Logger.Info("Merkaba 3D Tiles metrics " +
                    $"canonical={membrane.CanonicalOccupiedCount} " +
                    $"measured={membrane.MeasuredPatchCount} " +
                    $"inferred={membrane.InferredPatchCount} " +
                    $"tiles={result.TileCount} vertices={result.VertexCount} " +
                    $"triangles={result.TriangleCount} bytes={result.ByteLength} " +
                    $"archiveBytes={output.ArchiveBytes}");
                SetStatus($"3D Tiles: {result.TileCount} GLBs, " +
                    $"{result.TriangleCount} triangles, offline ZIP");
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
                if (File.Exists(ViewerPackagePath)) File.Delete(ViewerPackagePath);
                if (File.Exists(ViewerPackagePath + ".tmp"))
                    File.Delete(ViewerPackagePath + ".tmp");
                string legacyDirectory = Path.Combine(
                    Path.GetDirectoryName(ViewerPackagePath), ViewerPackageName);
                if (Directory.Exists(legacyDirectory))
                    Directory.Delete(legacyDirectory, true);
                if (Directory.Exists(legacyDirectory + ".tmp"))
                    Directory.Delete(legacyDirectory + ".tmp", true);
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

        private static byte[] LoadViewerResource(string resourceName)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null)
                throw new InvalidDataException(
                    $"Missing offline viewer resource {resourceName}.");
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

        internal static long WriteViewerArchive(string sourceDirectory,
            string destination)
        {
            string root = Path.GetFullPath(sourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string[] files = Directory.GetFiles(root, "*",
                SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            using (var stream = new FileStream(destination, FileMode.CreateNew,
                       FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
                       FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(stream,
                           ZipArchiveMode.Create, true))
                {
                    foreach (string file in files)
                    {
                        string relative = file.Substring(root.Length + 1)
                            .Replace(Path.DirectorySeparatorChar, '/');
                        ZipArchiveEntry entry = archive.CreateEntry(relative,
                            System.IO.Compression.CompressionLevel.NoCompression);
                        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1,
                            0, 0, 0, TimeSpan.Zero);
                        using Stream input = new FileStream(file, FileMode.Open,
                            FileAccess.Read, FileShare.Read, 1024 * 1024,
                            FileOptions.SequentialScan);
                        using Stream output = entry.Open();
                        input.CopyTo(output, 1024 * 1024);
                    }
                }
                stream.Flush(true);
                return stream.Length;
            }
        }

    }
}
