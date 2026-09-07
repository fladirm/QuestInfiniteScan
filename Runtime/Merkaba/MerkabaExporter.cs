using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Unity.Mathematics;
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
        private MerkabaPersistence _persistence;
        private RoomScanner _scanner;
        private DepthCapture _depthCapture;
        private MerkabaTileAddress[] _exportTiles;
        private MerkabaStorageAppendPosition _exportPosition;
        private float2 _exportPlaneBounds;

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
            _persistence = GetComponent<MerkabaPersistence>();
            _scanner = GetComponent<RoomScanner>();
            _depthCapture = GetComponent<DepthCapture>();
        }

        public async Task<bool> ExportGlbAsync()
        {
            if (IsExporting || _grid == null) return false;
            IsExporting = true;
            SetStatus("Exporting GLB…");
            string destination = ExportPath;
            string directory = Path.GetDirectoryName(destination);
            string temporary = destination + ".tmp";
            string spoolDirectory = temporary + ".parts";
            try
            {
                if (_integrator != null && _integrator.HasPendingObservation)
                    throw new InvalidOperationException(
                        "Export requires RoomScanner quiesce before readout.");
                IProgress<OperationWorkProgress> progress =
                    new Progress<OperationWorkProgress>(value =>
                        _scanner?.ReportOperation(
                            ScanOperationKind.ExportGlb, value));
                await RequireActiveSessionAnchorAsync();
                await _grid.FlushAllDirtyTilesAsync(progress);
                CaptureExportSource();
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(directory);
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (Directory.Exists(spoolDirectory))
                        Directory.Delete(spoolDirectory, true);
                });
                var metrics = new ExportMetrics();
                MerkabaGlbResult result;
                using (var streamSession =
                           new MerkabaGlbWriter.StreamingSession(
                           spoolDirectory))
                {
                    await StreamOwnedFlowersAsync(async (flower, _, _) =>
                    {
                        await Task.Run(() =>
                            streamSession.Append(flower, progress));
                        metrics.Add(flower);
                    }, progress, false);
                    metrics.DirtTriangles = await AppendDirtToGlbAsync(streamSession, progress);
                    result = await Task.Run(() =>
                    {
                        using var output = new FileStream(temporary,
                            FileMode.Create, FileAccess.Write, FileShare.None,
                            1024 * 1024, FileOptions.SequentialScan);
                        MerkabaGlbResult written = streamSession.Complete(
                            output, progress);
                        output.Flush(true);
                        return written;
                    });
                }

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
                            $"canonical={metrics.CanonicalOccupiedCount} " +
                            $"measuredPlane={metrics.MeasuredPlaneOccupiedCount} " +
                            $"confirmedL2={metrics.MeasuredPatchCount} " +
                            $"completedL2={metrics.InferredPatchCount} " +
                            $"unresolvedWedges={metrics.UnresolvedMeasuredPlaneCount} " +
                            $"dirt={metrics.DirtTriangles} " +
                            $"vertices={result.VertexCount} " +
                            $"triangles={result.PrimitiveCount} bytes={result.ByteLength}");
                SetStatus($"GLB: {result.PrimitiveCount} triangles, " +
                          $"{metrics.MeasuredPatchCount} measured, " +
                          $"{metrics.InferredPatchCount} completed, " +
                          $"{metrics.UnresolvedMeasuredPlaneCount} unresolved wedges");
                return true;
            }
            catch (Exception exception)
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (Directory.Exists(spoolDirectory))
                    Directory.Delete(spoolDirectory, true);
                Logger.Error("Merkaba GLB export failed: " + exception);
                SetStatus("Export failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsExporting = false;
                _exportTiles = null;
                _exportPosition = default;
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
                MerkabaSpatialBinding spatialBinding =
                    await CaptureSpatialBindingAsync();
                CaptureExportSource();
                byte[] viewerHtml = LoadViewerResource(ViewerResourceRoot);
                byte[] threeLicense = LoadViewerResource(
                    ViewerResourceRoot + "ThreeLicense");
                byte[] tilesLicense = LoadViewerResource(
                    ViewerResourceRoot + "TilesLicense");
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(exportDirectory);
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                    if (File.Exists(temporaryArchive))
                        File.Delete(temporaryArchive);
                });
                MerkabaTilesetResult result = await BuildStreamingTilesetAsync(
                    staging, spatialBinding, progress);
                long archiveBytes = await Task.Run(() =>
                {
                    File.WriteAllBytes(Path.Combine(staging, "index.html"),
                        viewerHtml);
                    File.WriteAllBytes(Path.Combine(staging,
                            "THIRD_PARTY_THREE_LICENSE.txt"), threeLicense);
                    File.WriteAllBytes(Path.Combine(staging,
                            "THIRD_PARTY_3DTILESRENDERERJS_LICENSE.txt"),
                        tilesLicense);
                    long bytes = WriteViewerArchive(staging,
                        temporaryArchive);
                    MerkabaFilePublishing.Publish(temporaryArchive,
                        destination);
                    return bytes;
                });
                LastExportPath = destination;
                Logger.Info("Merkaba 3D Tiles metrics " +
                    $"tiles={result.TileCount} vertices={result.VertexCount} " +
                    $"triangles={result.TriangleCount} bytes={result.ByteLength} " +
                    $"archiveBytes={archiveBytes}");
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
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
                if (File.Exists(temporaryArchive))
                    File.Delete(temporaryArchive);
                IsExporting = false;
                _exportTiles = null;
                _exportPosition = default;
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

        private async Task<MerkabaTilesetResult> BuildStreamingTilesetAsync(
            string staging, MerkabaSpatialBinding spatialBinding,
            IProgress<OperationWorkProgress> progress)
        {
            MerkabaTilesetWriter.BeginStreamingPackage(staging);
            var leaves = new List<MerkabaTilesetLeaf>();
            await StreamOwnedFlowersAsync(async (owned, groupIndex,
                groupCount) =>
            {
                int leafIndex = leaves.Count;
                MerkabaTilesetLeaf leaf = await Task.Run(() =>
                    MerkabaTilesetWriter.WriteStreamingLeaf(staging,
                        leafIndex, owned, progress));
                leaves.Add(leaf);
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.WritingFile, groupIndex + 1, groupCount,
                    $"Streamed spatial leaf {groupIndex + 1}/{groupCount}"));
            }, progress, true);
            await AppendDirtToTilesetAsync(staging, leaves, progress);
            return await Task.Run(() =>
                MerkabaTilesetWriter.CompleteStreamingPackage(staging,
                    leaves, spatialBinding));
        }

        // The same frozen source and shared direct/dual evaluator supplies
        // coverage. No caller can substitute an owner box or an all-uncovered
        // shortcut beside the final ordinary export path.
        internal Task<long> AppendDirtToGlbAsync(
            MerkabaGlbWriter.StreamingSession stream,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            RequireQuiescedDirtExport();
            return _grid.StreamStoredFlowerDirtAsync(_exportPlaneBounds, _exportTiles, _exportPosition,
                triangles => stream.AppendDirt(triangles, progress));
        }

        internal Task<long> AppendDirtToTilesetAsync(string staging,
            IList<MerkabaTilesetLeaf> leaves,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (leaves == null) throw new ArgumentNullException(nameof(leaves));
            RequireQuiescedDirtExport();
            return _grid.StreamStoredFlowerDirtAsync(_exportPlaneBounds, _exportTiles, _exportPosition, triangles =>
                leaves.Add(MerkabaTilesetWriter.WriteStreamingDirtLeaf(staging,
                    leaves.Count, triangles, progress)));
        }

        private float2 ExportPlaneBounds() => _depthCapture != null &&
            _depthCapture.TryGetFlowerPlaneBounds(out Vector2 bounds)
                ? new float2(bounds.x, bounds.y) : new float2(float.PositiveInfinity);

        private void CaptureExportSource()
        {
            _exportTiles = _grid.CaptureStoredFlowerSource(out _exportPosition);
            _exportPlaneBounds = ExportPlaneBounds();
        }

        private void RequireQuiescedDirtExport()
        {
            if (!IsExporting || _grid == null || _exportTiles == null ||
                (_integrator != null && _integrator.HasPendingObservation))
                throw new InvalidOperationException(
                    "DIRT export requires the active quiesced export transaction.");
        }

        private async Task<MerkabaSpatialBinding> CaptureSpatialBindingAsync()
        {
            RoomAnchorManager anchor = await RequireActiveSessionAnchorAsync();
            Matrix4x4 anchorFromPackage = anchor.SpatialAnchorMatrix.inverse *
                _grid.GridToWorldMatrix;
            var binding = new MerkabaSpatialBinding(anchor.SpatialAnchorUuid,
                anchorFromPackage);
            if (!binding.IsValid)
                throw new InvalidOperationException(
                    "3D Tiles spatial registration is not finite.");
            Logger.Info($"Merkaba 3D Tiles spatial binding " +
                $"anchor={binding.AnchorUuid:D}, " +
                $"packageOrigin={anchorFromPackage.GetColumn(3)}");
            return binding;
        }

        private async Task<RoomAnchorManager>
            RequireActiveSessionAnchorAsync()
        {
            Guid requiredUuid = _persistence != null
                ? _persistence.ActiveAnchorUuid : Guid.Empty;
            if (requiredUuid == Guid.Empty)
                throw new InvalidOperationException(
                    "Active session has no persisted room anchor.");
            RoomAnchorManager anchor = RoomAnchorManager.Instance;
            if (anchor == null || !anchor.enabled ||
                !await anchor.EnsureSessionAnchorAsync(requiredUuid, false) ||
                !anchor.HasSpatialAnchor ||
                anchor.SpatialAnchorUuid != requiredUuid)
                throw new InvalidOperationException(
                    "Active session room anchor could not be localized.");
            return anchor;
        }

        private async Task StreamOwnedFlowersAsync(
            Func<MerkabaFlowerPresentation, int, int, Task> consume,
            IProgress<OperationWorkProgress> progress, bool tilesetLeaves)
        {
            MerkabaTileAddress[] addresses = _exportTiles ?? throw new InvalidOperationException("Flower export has no frozen source.");
            float2 planeBounds = _exportPlaneBounds;
            for (int index = 0; index < addresses.Length; index++)
            {
                var reader = await _grid.ReadStoredFlowerContextAsync(addresses[index], addresses, _exportPosition)
                    .ConfigureAwait(false);
                MerkabaTileAddress address = addresses[index];
                MerkabaFlowerPresentation presentation = await Task.Run(() =>
                    MerkabaFlowerPresentation.Build(reader, address, planeBounds)).ConfigureAwait(false);
                if (presentation.TriangleCount != 0)
                    await consume(presentation, index, addresses.Length).ConfigureAwait(false);
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.BuildingMerkabaGeometry, index + 1, addresses.Length,
                    $"Evaluated fixed L2 Flower tile {index + 1}/{addresses.Length}"));
                if (tilesetLeaves)
                    Logger.Info($"Merkaba 3D Tiles Flower owner={address} " +
                        $"occupiedOwners={presentation.OccupiedOwners} carriers={presentation.Carriers.Count} " +
                        $"triangles={presentation.TriangleCount} unresolvedWedges={presentation.UnresolvedWedges}");
            }
        }

        private sealed class ExportMetrics
        {
            internal long CanonicalOccupiedCount;
            internal long MeasuredPlaneOccupiedCount;
            internal long MeasuredPatchCount;
            internal long InferredPatchCount;
            internal long UnresolvedMeasuredPlaneCount;
            internal long DirtTriangles;

            internal void Add(MerkabaFlowerPresentation result)
            {
                CanonicalOccupiedCount += result.OccupiedOwners;
                MeasuredPlaneOccupiedCount += result.OccupiedOwners;
                MeasuredPatchCount += result.TriangleCount;
                UnresolvedMeasuredPlaneCount += result.UnresolvedWedges;
                foreach (var carrier in result.Carriers)
                    InferredPatchCount += math.countbits(carrier.Symbol.CompletedWedgeMask);
            }
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
