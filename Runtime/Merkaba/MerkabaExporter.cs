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
                await _grid.FlushAllDirtyTilesAsync(progress);
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
                    await StreamOwnedMembranesAsync(async (membrane, _, _) =>
                    {
                        await Task.Run(() =>
                            streamSession.Append(membrane, progress));
                        metrics.Add(membrane);
                    }, progress);
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
                            $"membraneMeasured={metrics.MeasuredPatchCount} " +
                            $"inferredGray={metrics.InferredPatchCount} " +
                            $"legacy={metrics.LegacyMeasuredUnknownPlaneCount} " +
                            $"removed={metrics.RemovedBehindMembraneCount} " +
                            $"vertices={result.VertexCount} " +
                            $"triangles={result.PrimitiveCount} bytes={result.ByteLength}");
                SetStatus($"GLB: {result.PrimitiveCount} triangles, " +
                          $"{metrics.MeasuredPatchCount} measured, " +
                          $"{metrics.InferredPatchCount} gray inferred, " +
                          $"{metrics.LegacyMeasuredUnknownPlaneCount} legacy planes");
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
            await StreamOwnedMembranesAsync(async (owned, groupIndex,
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
            }, progress);
            return await Task.Run(() =>
                MerkabaTilesetWriter.CompleteStreamingPackage(staging,
                    leaves, spatialBinding));
        }

        private async Task<MerkabaSpatialBinding> CaptureSpatialBindingAsync()
        {
            RoomAnchorManager anchor = RoomAnchorManager.Instance;
            if (anchor == null || !anchor.enabled)
                throw new InvalidOperationException(
                    "3D Tiles export requires RoomAnchorManager.");
            if (!await anchor.EnsureSpatialAnchorAsync() ||
                !anchor.HasSpatialAnchor ||
                anchor.SpatialAnchorUuid == Guid.Empty)
                throw new InvalidOperationException(
                    "3D Tiles export could not create and localize its " +
                    "persistent spatial anchor.");
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

        private async Task StreamOwnedMembranesAsync(
            Func<MerkabaExportMembraneResult, int, int, Task> consume,
            IProgress<OperationWorkProgress> progress)
        {
            MerkabaTileAddress[] addresses = _grid.CaptureStoredTileIndex();
            var available = new HashSet<MerkabaTileAddress>(addresses);
            var ownerGroups = new Dictionary<MerkabaTileAddress,
                List<MerkabaTileAddress>>();
            foreach (MerkabaTileAddress address in addresses)
            {
                var key = new MerkabaTileAddress(address.BlockCoord,
                    (uint)address.ChunkLocal);
                if (!ownerGroups.TryGetValue(key,
                        out List<MerkabaTileAddress> owners))
                {
                    owners = new List<MerkabaTileAddress>(
                        MerkabaSpatial.TilesPerChunk);
                    ownerGroups.Add(key, owners);
                }
                owners.Add(address);
            }
            var keys = new List<MerkabaTileAddress>(ownerGroups.Keys);
            keys.Sort();
            for (int groupIndex = 0; groupIndex < keys.Count; groupIndex++)
            {
                MerkabaTileAddress ownerKey = keys[groupIndex];
                List<MerkabaTileAddress> owners = ownerGroups[ownerKey];
                owners.Sort();
                var contextSet = new HashSet<MerkabaTileAddress>(owners);
                foreach (MerkabaTileAddress owner in owners)
                {
                    int3 origin = MerkabaSpatial.Decode(owner.BlockCoord,
                        owner.LocalAddress, 0);
                    for (int z = -1; z <= 1; z++)
                    for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        MerkabaSpatial.Address neighbour =
                            MerkabaSpatial.Encode(origin +
                            new int3(x, y, z) * MerkabaSpatial.TileSize);
                        var neighbourAddress = new MerkabaTileAddress(
                            neighbour.BlockCoord,
                            (uint)(neighbour.ChunkLocal |
                            (neighbour.TileLocal << 9)));
                        if (available.Contains(neighbourAddress))
                            contextSet.Add(neighbourAddress);
                    }
                }
                var context = new List<MerkabaTileAddress>(contextSet);
                context.Sort();
                var evidence = new Dictionary<int3, KernelState>(
                    context.Count * MerkabaSpatial.KernelsPerTile / 4);
                for (int offset = 0; offset < context.Count;
                     offset += MerkabaGrid.StreamBatchCapacity)
                {
                    int count = Math.Min(MerkabaGrid.StreamBatchCapacity,
                        context.Count - offset);
                    var batch = context.GetRange(offset, count);
                    MerkabaTileSnapshot[] tiles = await _grid
                        .ReadStoredTilesAsync(batch).ConfigureAwait(false);
                    foreach (MerkabaTileSnapshot tile in tiles)
                    for (int kernel = 0; kernel < tile.States.Length; kernel++)
                    {
                        KernelState state = tile.States[kernel];
                        if (state.OccupancyEvidence == 0 && state.Flags == 0u &&
                            state.ColorConfidence == 0u) continue;
                        int3 coord = MerkabaSpatial.Decode(
                            tile.Address.BlockCoord, tile.Address.LocalAddress,
                            kernel);
                        evidence.Add(coord, state);
                    }
                }
                bool hasOwnerSurface = false;
                foreach (KeyValuePair<int3, KernelState> pair in evidence)
                    if (IsOwnedByChunk(pair.Key, ownerKey) &&
                        pair.Value.IsOccupied)
                    {
                        hasOwnerSurface = true;
                        break;
                    }
                if (!hasOwnerSurface) continue;
                MerkabaExportMembraneResult local = await Task.Run(() =>
                    MerkabaExportMembrane.Build(
                        MerkabaExportShell.Build(evidence)));
                MerkabaExportMembraneResult owned = OwnChunk(local, ownerKey);
                if (owned.Patches.Count == 0 && owned.LegacyKernels.Count == 0)
                    continue;
                await consume(owned, groupIndex, keys.Count);
            }
        }

        private static bool IsOwnedByChunk(int3 coord,
            MerkabaTileAddress ownerKey)
        {
            MerkabaSpatial.Address address = MerkabaSpatial.Encode(coord);
            return math.all(address.BlockCoord == ownerKey.BlockCoord) &&
                address.ChunkLocal == ownerKey.ChunkLocal;
        }

        private static MerkabaExportMembraneResult OwnChunk(
            MerkabaExportMembraneResult source, MerkabaTileAddress ownerKey)
        {
            var patches = source.Patches.FindAll(patch =>
                IsOwnedByChunk(patch.Coord, ownerKey));
            var legacy = source.LegacyKernels.FindAll(kernel =>
                IsOwnedByChunk(kernel.Coord, ownerKey));
            int measured = 0;
            int inferred = 0;
            foreach (MerkabaExportMembranePatch patch in patches)
                if (patch.IsInferred) inferred++;
                else measured++;
            int canonicalOwned = 0;
            foreach (int3 coord in source.CanonicalOccupiedCoordinates)
                if (IsOwnedByChunk(coord, ownerKey)) canonicalOwned++;
            var removedBehind = new List<int3>();
            foreach (int3 coord in source.RemovedBehindCoordinates)
                if (IsOwnedByChunk(coord, ownerKey)) removedBehind.Add(coord);
            return new MerkabaExportMembraneResult(patches, legacy,
                source.CanonicalOccupiedCoordinates,
                canonicalOwned, canonicalOwned - legacy.Count,
                measured, inferred,
                legacy.Count, source.UnresolvedLegacyCount,
                removedBehind.ToArray(), removedBehind.Count,
                source.PartitionCutCount);
        }

        private sealed class ExportMetrics
        {
            internal long CanonicalOccupiedCount;
            internal long MeasuredPlaneOccupiedCount;
            internal long MeasuredPatchCount;
            internal long InferredPatchCount;
            internal long LegacyMeasuredUnknownPlaneCount;
            internal long RemovedBehindMembraneCount;

            internal void Add(MerkabaExportMembraneResult result)
            {
                CanonicalOccupiedCount += result.CanonicalOccupiedCount;
                MeasuredPlaneOccupiedCount += result.MeasuredPlaneOccupiedCount;
                MeasuredPatchCount += result.MeasuredPatchCount;
                InferredPatchCount += result.InferredPatchCount;
                LegacyMeasuredUnknownPlaneCount +=
                    result.LegacyMeasuredUnknownPlaneCount;
                RemovedBehindMembraneCount +=
                    result.RemovedBehindMembraneCount;
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
