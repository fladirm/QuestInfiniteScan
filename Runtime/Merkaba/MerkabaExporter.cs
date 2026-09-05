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
        private const int ExportTileCacheCapacity = 512;

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private MerkabaPersistence _persistence;
        private RoomScanner _scanner;
        private bool _publicSavePending;
        private string _publicSaveName;

        public bool IsExporting { get; private set; }
        public string LastExportPath { get; private set; }
        public string LastPublicExportUri { get; private set; }
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
        }

        public async Task<bool> ExportGlbAsync() =>
            await ExportGlbAsync(null);

        public async Task<bool> ExportGlbAsync(string suggestedFileName)
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
                    await StreamOwnedMembranesAsync(async (membrane, _, _, _) =>
                    {
                        await Task.Run(() =>
                            streamSession.Append(membrane, progress));
                        metrics.Add(membrane);
                    }, progress, false);
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
                            $"unresolvedPlane={metrics.UnresolvedMeasuredPlaneCount} " +
                            $"removed={metrics.RemovedBehindMembraneCount} " +
                            $"vertices={result.VertexCount} " +
                            $"triangles={result.PrimitiveCount} bytes={result.ByteLength}");
                SetStatus($"GLB: {result.PrimitiveCount} triangles, " +
                          $"{metrics.MeasuredPatchCount} measured, " +
                          $"{metrics.InferredPatchCount} gray inferred, " +
                          $"{metrics.UnresolvedMeasuredPlaneCount} unresolved planes");
                RequestPublicSave(destination,
                    ExportFileNameFor(suggestedFileName, ".glb"),
                    "model/gltf-binary");
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

        public async Task<bool> ExportViewerPackageAsync() =>
            await ExportViewerPackageAsync(null);

        public async Task<bool> ExportViewerPackageAsync(
            string suggestedFileName)
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
                RequestPublicSave(destination,
                    ExportFileNameFor(suggestedFileName, ".zip"),
                    "application/zip");
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

        internal static string SanitizeExportFileName(string requested,
            string fallback, string extension)
        {
            string value = string.IsNullOrWhiteSpace(requested)
                ? fallback : requested.Trim();
            value = Path.GetFileName(value);
            if (value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - extension.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            var clean = new char[value.Length];
            int length = 0;
            foreach (char character in value)
            {
                bool forbidden = character == '/' || character == '\\' ||
                    character == ':' || character == '*' || character == '?' ||
                    character == '"' || character == '<' || character == '>' ||
                    character == '|';
                if (!forbidden)
                    for (int index = 0; index < invalid.Length; index++)
                        if (character == invalid[index])
                        {
                            forbidden = true;
                            break;
                        }
                char output = forbidden ? '-' : character;
                if (output == '-' && length > 0 && clean[length - 1] == '-')
                    continue;
                clean[length++] = output;
            }
            string stem = new string(clean, 0, length).Trim(' ', '.', '-');
            if (string.IsNullOrWhiteSpace(stem)) stem = fallback;
            return stem + extension;
        }

        private string ExportFileNameFor(string requested, string extension)
        {
            string fallback = _persistence != null &&
                _persistence.ActiveSessionId != Guid.Empty
                ? _persistence.ActiveSessionName : ViewerPackageName;
            return SanitizeExportFileName(requested, fallback, extension);
        }

        private void RequestPublicSave(string sourcePath, string suggestedName,
            string mimeType)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_publicSavePending)
            {
                SetStatus("Export ready in app storage; a Save As picker is " +
                    "already open");
                return;
            }
            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<
                    AndroidJavaObject>("currentActivity");
                using var picker = new AndroidJavaClass(
                    "com.genesis.roomscan.MerkabaPackagePicker");
                _publicSavePending = true;
                _publicSaveName = suggestedName;
                SetStatus("Choose where to save " + suggestedName);
                picker.CallStatic("save", activity, sourcePath, suggestedName,
                    mimeType, gameObject.name,
                    nameof(OnExportDocumentResult));
            }
            catch (Exception exception)
            {
                _publicSavePending = false;
                Logger.Error("Could not open export Save As picker: " +
                    exception);
                SetStatus("Export ready in app storage; Save As failed: " +
                    exception.Message);
            }
#endif
        }

        /// <summary>Android document-picker callback via UnitySendMessage.</summary>
        public void OnExportDocumentResult(string result)
        {
            _publicSavePending = false;
            if (string.IsNullOrWhiteSpace(result) || result == "CANCELLED")
            {
                SetStatus("Save As cancelled; export remains in app storage");
                return;
            }
            const string savedPrefix = "SAVED:";
            if (result.StartsWith(savedPrefix, StringComparison.Ordinal))
            {
                LastPublicExportUri = result.Substring(savedPrefix.Length);
                SetStatus((_publicSaveName ?? "Export") +
                    " saved to Quest Files");
                _publicSaveName = null;
                return;
            }
            const string errorPrefix = "ERROR:";
            string detail = result.StartsWith(errorPrefix,
                StringComparison.Ordinal)
                ? result.Substring(errorPrefix.Length) : result;
            Logger.Error("Export Save As failed: " + detail);
            SetStatus("Export remains in app storage; Save As failed: " +
                detail);
        }

        private async Task<MerkabaTilesetResult> BuildStreamingTilesetAsync(
            string staging, MerkabaSpatialBinding spatialBinding,
            IProgress<OperationWorkProgress> progress)
        {
            MerkabaTilesetWriter.BeginStreamingPackage(staging);
            var leaves = new List<MerkabaTilesetLeaf>();
            MerkabaTilesetWriter.StreamingLeafBuilder leafBuilder = null;
            int3 activeBlock = default;
            int streamedGroups = 0;

            async Task CompleteLeafAsync()
            {
                if (leafBuilder == null) return;
                MerkabaTilesetWriter.StreamingLeafBuilder completed =
                    leafBuilder;
                leafBuilder = null;
                try
                {
                    MerkabaTilesetLeaf leaf = await Task.Run(() =>
                        completed.Complete(progress));
                    leaves.Add(leaf);
                }
                finally
                {
                    completed.Dispose();
                }
            }

            try
            {
                await StreamOwnedMembranesAsync(async (owned, ownerKey,
                    groupIndex, groupCount) =>
                {
                    if (leafBuilder != null &&
                        (!math.all(activeBlock == ownerKey.BlockCoord) ||
                         leafBuilder.EstimatedCompleteByteLength >=
                         MerkabaTilesetWriter.DefaultTargetLeafBytes))
                        await CompleteLeafAsync();
                    if (leafBuilder == null)
                    {
                        activeBlock = ownerKey.BlockCoord;
                        leafBuilder = new MerkabaTilesetWriter
                            .StreamingLeafBuilder(staging, leaves.Count,
                            MerkabaTilesetWriter.BlockLocalOrigin(activeBlock));
                    }
                    MerkabaTilesetWriter.StreamingLeafBuilder target =
                        leafBuilder;
                    await Task.Run(() => target.Append(owned, progress));
                    if (target.EstimatedCompleteByteLength >=
                        MerkabaTilesetWriter.DefaultTargetLeafBytes)
                        await CompleteLeafAsync();
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.WritingFile, groupIndex + 1,
                        groupCount, $"Streamed spatial group " +
                        $"{groupIndex + 1}/{groupCount}"));
                    streamedGroups++;
                }, progress, true);
                await CompleteLeafAsync();
            }
            finally
            {
                leafBuilder?.Dispose();
            }
            Logger.Info("Merkaba 3D Tiles spatial batching " +
                $"groups={streamedGroups} leaves={leaves.Count} " +
                $"targetBytes={MerkabaTilesetWriter.DefaultTargetLeafBytes}");
            return await Task.Run(() =>
                MerkabaTilesetWriter.CompleteStreamingPackage(staging,
                    leaves, spatialBinding));
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

        private async Task StreamOwnedMembranesAsync(
            Func<MerkabaExportMembraneResult, MerkabaTileAddress, int, int,
                Task> consume,
            IProgress<OperationWorkProgress> progress, bool tilesetLeaves)
        {
            MerkabaTileAddress[] addresses = _grid.CaptureStoredTileIndex();
            if (addresses.Length == 0)
                throw new InvalidDataException(
                    "Export invariant failed at stored canonical data: " +
                    "storedTiles=0.");
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
            long totalNonzeroStates = 0L;
            long totalOccupiedOwners = 0L;
            long totalMeasuredOwners = 0L;
            int emittedGroups = 0;
            var tileCache = new ExportTileSnapshotCache(_grid,
                ExportTileCacheCapacity);
            for (int groupIndex = 0; groupIndex < keys.Count; groupIndex++)
            {
                MerkabaTileAddress ownerKey = keys[groupIndex];
                List<MerkabaTileAddress> owners = ownerGroups[ownerKey];
                owners.Sort();
                var ownerSet = new HashSet<MerkabaTileAddress>(owners);
                int nonzeroStates = 0;
                int occupiedOwners = 0;
                int measuredOwners = 0;
                int membranePatches = 0;
                int ownedPatches = 0;
                bool emittedLeaf = false;
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
                MerkabaTileSnapshot[] tiles = await tileCache.ReadAsync(
                    context).ConfigureAwait(false);
                foreach (MerkabaTileSnapshot tile in tiles)
                {
                    bool ownerTile = ownerSet.Contains(tile.Address);
                    for (int kernel = 0; kernel < tile.States.Length;
                         kernel++)
                    {
                        KernelState state = tile.States[kernel];
                        if (state.OccupancyEvidence == 0 &&
                            state.Flags == 0u &&
                            state.ColorConfidence == 0u) continue;
                        if (ownerTile)
                        {
                            nonzeroStates++;
                            if (state.IsOccupied)
                            {
                                occupiedOwners++;
                                if (state.HasMeasuredSurfacePlane)
                                    measuredOwners++;
                            }
                        }
                        int3 coord = MerkabaSpatial.Decode(
                            tile.Address.BlockCoord,
                            tile.Address.LocalAddress, kernel);
                        evidence.Add(coord, state);
                    }
                }
                totalNonzeroStates += nonzeroStates;
                totalOccupiedOwners += occupiedOwners;
                totalMeasuredOwners += measuredOwners;
                if (occupiedOwners == 0)
                {
                    LogOwnerGroup(tilesetLeaves, ownerKey, owners.Count,
                        nonzeroStates, occupiedOwners, measuredOwners,
                        membranePatches, ownedPatches, emittedLeaf);
                    continue;
                }
                MerkabaExportMembraneResult local;
                try
                {
                    local = await Task.Run(() => MerkabaExportMembrane.Build(
                        MerkabaExportShell.Build(evidence)));
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        "Export invariant failed at measured membrane for " +
                        $"owner {OwnerChunkLabel(ownerKey)}: " +
                        $"storedTiles={owners.Count} " +
                        $"nonzeroStates={nonzeroStates} " +
                        $"occupiedOwners={occupiedOwners} " +
                        $"measuredOwners={measuredOwners}. " +
                        exception.Message, exception);
                }
                membranePatches = local.Patches.Count;
                MerkabaExportMembraneResult owned = OwnChunk(local, ownerKey);
                ownedPatches = owned.Patches.Count;
                ValidateOwnedMeasuredPatches(ownerKey, owners.Count,
                    nonzeroStates, occupiedOwners, measuredOwners,
                    membranePatches, ownedPatches);
                if (ownedPatches == 0)
                {
                    LogOwnerGroup(tilesetLeaves, ownerKey, owners.Count,
                        nonzeroStates, occupiedOwners, measuredOwners,
                        membranePatches, ownedPatches, emittedLeaf);
                    continue;
                }
                await consume(owned, ownerKey, groupIndex, keys.Count);
                emittedLeaf = true;
                emittedGroups++;
                LogOwnerGroup(tilesetLeaves, ownerKey, owners.Count,
                    nonzeroStates, occupiedOwners, measuredOwners,
                    membranePatches, ownedPatches, emittedLeaf);
            }
            if (emittedGroups == 0)
                throw new InvalidDataException(
                    "Export produced no consumable owner groups: " +
                    $"storedTiles={addresses.Length} " +
                    $"nonzeroStates={totalNonzeroStates} " +
                    $"occupiedOwners={totalOccupiedOwners} " +
                    $"measuredOwners={totalMeasuredOwners} " +
                    "emittedLeaf=0.");
            Logger.Info("Merkaba export tile IO " +
                $"logicalReads={tileCache.LogicalReadCount} " +
                $"physicalReads={tileCache.PhysicalReadCount} " +
                $"cacheHits={tileCache.CacheHitCount} " +
                $"capacity={ExportTileCacheCapacity}");
        }

        private static void LogOwnerGroup(bool enabled,
            MerkabaTileAddress ownerKey, int storedTiles, int nonzeroStates,
            int occupiedOwners, int measuredOwners, int membranePatches,
            int ownedPatches, bool emittedLeaf)
        {
            if (!enabled) return;
            Logger.Info("Merkaba 3D Tiles owner=" +
                $"{OwnerChunkLabel(ownerKey)} storedTiles={storedTiles} " +
                $"nonzeroStates={nonzeroStates} " +
                $"occupiedOwners={occupiedOwners} " +
                $"measuredOwners={measuredOwners} " +
                $"membranePatches={membranePatches} " +
                $"ownedPatches={ownedPatches} " +
                $"emittedLeaf={(emittedLeaf ? 1 : 0)}");
        }

        private static string OwnerChunkLabel(MerkabaTileAddress key) =>
            $"({key.BlockCoord.x},{key.BlockCoord.y},{key.BlockCoord.z})/" +
            key.ChunkLocal;

        internal static void ValidateOwnedMeasuredPatches(
            MerkabaTileAddress ownerKey, int storedTiles, int nonzeroStates,
            int occupiedOwners, int measuredOwners, int membranePatches,
            int ownedPatches)
        {
            if (occupiedOwners == 0 || measuredOwners == 0 ||
                ownedPatches != 0) return;
            throw new InvalidDataException(
                "Export invariant failed at spatial ownership for " +
                $"owner {OwnerChunkLabel(ownerKey)}: " +
                $"storedTiles={storedTiles} " +
                $"nonzeroStates={nonzeroStates} " +
                $"occupiedOwners={occupiedOwners} " +
                $"measuredOwners={measuredOwners} " +
                $"membranePatches={membranePatches} " +
                "ownedPatches=0 emittedLeaf=0.");
        }

        private static bool IsOwnedByChunk(int3 coord,
            MerkabaTileAddress ownerKey)
        {
            MerkabaSpatial.Address address = MerkabaSpatial.Encode(coord);
            return math.all(address.BlockCoord == ownerKey.BlockCoord) &&
                address.ChunkLocal == ownerKey.ChunkLocal;
        }

        internal static MerkabaExportMembraneResult OwnChunk(
            MerkabaExportMembraneResult source, MerkabaTileAddress ownerKey)
        {
            var patches = source.Patches.FindAll(patch =>
                IsOwnedByChunk(patch.Coord, ownerKey));
            int measured = 0;
            int inferred = 0;
            foreach (MerkabaExportMembranePatch patch in patches)
                if (patch.IsInferred) inferred++;
                else measured++;
            var canonicalOwned = new List<int3>();
            foreach (int3 coord in source.CanonicalOccupiedCoordinates)
                if (IsOwnedByChunk(coord, ownerKey)) canonicalOwned.Add(coord);
            var measuredOwned = new List<int3>();
            foreach (int3 coord in source.MeasuredPlaneCoordinates)
                if (IsOwnedByChunk(coord, ownerKey)) measuredOwned.Add(coord);
            var removedBehind = new List<int3>();
            foreach (int3 coord in source.RemovedBehindCoordinates)
                if (IsOwnedByChunk(coord, ownerKey)) removedBehind.Add(coord);
            return new MerkabaExportMembraneResult(patches,
                canonicalOwned.ToArray(), measuredOwned.ToArray(),
                measured, inferred, removedBehind.ToArray(),
                source.PartitionCutCount);
        }

        private sealed class ExportMetrics
        {
            internal long CanonicalOccupiedCount;
            internal long MeasuredPlaneOccupiedCount;
            internal long MeasuredPatchCount;
            internal long InferredPatchCount;
            internal long UnresolvedMeasuredPlaneCount;
            internal long RemovedBehindMembraneCount;

            internal void Add(MerkabaExportMembraneResult result)
            {
                CanonicalOccupiedCount += result.CanonicalOccupiedCount;
                MeasuredPlaneOccupiedCount += result.MeasuredPlaneOccupiedCount;
                MeasuredPatchCount += result.MeasuredPatchCount;
                InferredPatchCount += result.InferredPatchCount;
                UnresolvedMeasuredPlaneCount +=
                    result.UnresolvedMeasuredPlaneCount;
                RemovedBehindMembraneCount +=
                    result.RemovedBehindMembraneCount;
            }
        }

        private sealed class ExportTileSnapshotCache
        {
            private sealed class Entry
            {
                internal readonly MerkabaTileSnapshot Snapshot;
                internal readonly LinkedListNode<MerkabaTileAddress> Node;

                internal Entry(MerkabaTileSnapshot snapshot,
                    LinkedListNode<MerkabaTileAddress> node)
                {
                    Snapshot = snapshot;
                    Node = node;
                }
            }

            private readonly MerkabaGrid _grid;
            private readonly int _capacity;
            private readonly Dictionary<MerkabaTileAddress, Entry> _entries;
            private readonly LinkedList<MerkabaTileAddress> _recency = new();

            internal long LogicalReadCount { get; private set; }
            internal long PhysicalReadCount { get; private set; }
            internal long CacheHitCount { get; private set; }

            internal ExportTileSnapshotCache(MerkabaGrid grid, int capacity)
            {
                _grid = grid ?? throw new ArgumentNullException(nameof(grid));
                _capacity = Math.Max(MerkabaGrid.StreamBatchCapacity,
                    capacity);
                _entries = new Dictionary<MerkabaTileAddress, Entry>(
                    _capacity);
            }

            internal async Task<MerkabaTileSnapshot[]> ReadAsync(
                IReadOnlyList<MerkabaTileAddress> addresses)
            {
                if (addresses == null)
                    throw new ArgumentNullException(nameof(addresses));
                var result = new MerkabaTileSnapshot[addresses.Count];
                LogicalReadCount += addresses.Count;
                for (int offset = 0; offset < addresses.Count;)
                {
                    if (TryGet(addresses[offset], out result[offset]))
                    {
                        CacheHitCount++;
                        offset++;
                        continue;
                    }

                    var missing = new List<MerkabaTileAddress>(
                        MerkabaGrid.StreamBatchCapacity);
                    var resultIndices = new List<int>(
                        MerkabaGrid.StreamBatchCapacity);
                    while (offset < addresses.Count && missing.Count <
                           MerkabaGrid.StreamBatchCapacity)
                    {
                        MerkabaTileAddress address = addresses[offset];
                        if (TryGet(address, out result[offset]))
                            CacheHitCount++;
                        else
                        {
                            missing.Add(address);
                            resultIndices.Add(offset);
                        }
                        offset++;
                    }
                    if (missing.Count == 0) continue;
                    MerkabaTileSnapshot[] loaded = await _grid
                        .ReadStoredTilesAsync(missing).ConfigureAwait(false);
                    if (loaded.Length != missing.Count)
                        throw new InvalidDataException(
                            "M8 export tile read count mismatch.");
                    PhysicalReadCount += loaded.Length;
                    for (int index = 0; index < loaded.Length; index++)
                    {
                        MerkabaTileSnapshot snapshot = loaded[index];
                        if (!snapshot.Address.Equals(missing[index]))
                            throw new InvalidDataException(
                                "M8 export tile read order mismatch.");
                        result[resultIndices[index]] = snapshot;
                        Add(snapshot);
                    }
                }
                return result;
            }

            private bool TryGet(MerkabaTileAddress address,
                out MerkabaTileSnapshot snapshot)
            {
                if (!_entries.TryGetValue(address, out Entry entry))
                {
                    snapshot = null;
                    return false;
                }
                _recency.Remove(entry.Node);
                _recency.AddLast(entry.Node);
                snapshot = entry.Snapshot;
                return true;
            }

            private void Add(MerkabaTileSnapshot snapshot)
            {
                var node = new LinkedListNode<MerkabaTileAddress>(
                    snapshot.Address);
                _recency.AddLast(node);
                _entries.Add(snapshot.Address, new Entry(snapshot, node));
                if (_entries.Count <= _capacity) return;
                LinkedListNode<MerkabaTileAddress> oldest = _recency.First;
                _recency.RemoveFirst();
                _entries.Remove(oldest.Value);
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
