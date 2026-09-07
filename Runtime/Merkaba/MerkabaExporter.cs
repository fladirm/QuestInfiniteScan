using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>On-demand offline GLB PBR readout of the canonical Merkaba grid.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaExporter : MonoBehaviour
    {
        private const string ViewerPackageName = "QuestMerkabaScan";
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
        private string _lastGlbPath;
        private string _lastViewerPackagePath;

        public bool IsExporting { get; private set; }
        public string LastExportPath { get; private set; }
        public string LastStatus { get; private set; } = "Not exported";
        public string ExportPath => _lastGlbPath ?? ExportPathFor(null, ".glb");
        public string ViewerPackagePath => _lastViewerPackagePath ??
            ExportPathFor(null, ".zip");
        public event Action StatusChanged;

        internal bool HasResumeReceipt(string fileName, bool tiles) =>
            MerkabaExportJournal.Exists(ExportPathFor(fileName, tiles ? ".zip" : ".glb") + ".resume");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            _persistence = GetComponent<MerkabaPersistence>();
            _scanner = GetComponent<RoomScanner>();
            _depthCapture = GetComponent<DepthCapture>();
        }

        public Task<bool> ExportGlbAsync() => ExportGlbAsync(null);

        public Task<bool> ExportGlbAsync(string fileName,
            CancellationToken cancellationToken = default) => _scanner != null
            ? _scanner.ExportGlbAsync(fileName, cancellationToken)
            : Task.FromResult(false);

        internal async Task<bool> ExportGlbCoreAsync(string fileName,
            CancellationToken cancellationToken)
        {
            if (IsExporting || _grid == null) return false;
            RequireExportLease();
            string destination = ExportPathFor(fileName, ".glb");
            string directory = Path.GetDirectoryName(destination);
            string temporary = destination + ".tmp";
            string journalDirectory = destination + ".resume";
            string spoolDirectory = Path.Combine(journalDirectory, "content");
            MerkabaExportJournal journal = null;
            bool published = false;
            IsExporting = true;
            try
            {
                SetStatus("Exporting GLB…");
                cancellationToken.ThrowIfCancellationRequested();
                IProgress<OperationWorkProgress> progress =
                    new Progress<OperationWorkProgress>(value =>
                        _scanner?.ReportOperation(
                            ScanOperationKind.ExportGlb, value));
                CaptureExportSource();
                MerkabaNativePackage.Source nativeSource = _persistence.CaptureExportPackageSource();
                using MerkabaNativePackage.Snapshot nativePackage = await Task.Run(() =>
                    MerkabaNativePackage.Capture(nativeSource, cancellationToken));
                journal = await Task.Run(() => new MerkabaExportJournal(journalDirectory,
                    nativePackage, false, _exportPlaneBounds, null, null, cancellationToken));
                if (journal.Current.nextTile > _exportTiles.Length ||
                    (journal.Current.stage != 0 && journal.Current.nextTile != _exportTiles.Length) ||
                    (journal.Current.glb == null && (journal.Current.stage != 0 ||
                        journal.Current.nextTile != 0 || journal.Current.nextDirtPacket != 0)))
                    throw new InvalidDataException("Export cursor does not belong to the frozen source index.");
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(directory);
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (journal.Current.glb == null && Directory.Exists(spoolDirectory))
                        Directory.Delete(spoolDirectory, true);
                });
                var metrics = new ExportMetrics(journal.Current);
                MerkabaGlbResult result;
                using (var streamSession = await Task.Run(() =>
                           new MerkabaGlbWriter.StreamingSession(spoolDirectory,
                               journal.Current.glb, true, cancellationToken)))
                {
                    streamSession.AttachNativePackage(nativePackage);
                    if (journal.Current.glb == null)
                        await Task.Run(() => CheckpointGlb(journal, streamSession,
                            metrics, 0, 0, cancellationToken));
                    if (journal.Current.stage == 0)
                    {
                        await StreamOwnedFlowersAsync(async (flower, _, _) =>
                        {
                            await Task.Run(() => streamSession.Append(flower, progress,
                                cancellationToken: cancellationToken));
                            metrics.Add(flower);
                        }, progress, false, cancellationToken, journal.Current.nextTile,
                            next => Task.Run(() => CheckpointGlb(journal, streamSession,
                                metrics, 0, next, cancellationToken)));
                        await Task.Run(() => CheckpointGlb(journal, streamSession,
                            metrics, 1, _exportTiles.Length, cancellationToken));
                    }
                    if (journal.Current.stage == 1)
                    {
                        metrics.DirtTriangles = await AppendDirtToGlbAsync(streamSession,
                            progress, cancellationToken, journal, metrics);
                        await Task.Run(() => CheckpointGlb(journal, streamSession,
                            metrics, 2, _exportTiles.Length, cancellationToken));
                    }
                    result = await Task.Run(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var output = new FileStream(temporary,
                            FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            1024 * 1024, FileOptions.SequentialScan);
                        MerkabaGlbResult written = streamSession.Complete(
                            output, progress, cancellationToken);
                        output.Flush(true);
                        return written;
                    });
                }

                progress.Report(new OperationWorkProgress(
                    ScanOperationStage.PublishingFile, 0, 1,
                    "Publishing durable GLB"));
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MerkabaFilePublishing.Publish(temporary, destination);
                });
                published = true;
                progress.Report(new OperationWorkProgress(
                    ScanOperationStage.PublishingFile, 1, 1,
                    "GLB published"));
                LastExportPath = destination;
                _lastGlbPath = destination;
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
                await RecordExportFailureAsync(journal, exception);
                ReportExportFailure(exception);
                if (journal != null) SetStatus(LastStatus + "; retry this export name to resume its verified cursor");
                return false;
            }
            finally
            {
                journal?.Dispose();
                if (journal != null) CleanupExportStaging(temporary, published ? journalDirectory : null);
                IsExporting = false;
                _exportTiles = null;
                _exportPosition = default;
                StatusChanged?.Invoke();
            }
        }

        public Task<bool> ExportViewerPackageAsync() =>
            ExportViewerPackageAsync(null);

        public Task<bool> ExportViewerPackageAsync(string fileName,
            CancellationToken cancellationToken = default) => _scanner != null
            ? _scanner.ExportViewerPackageAsync(fileName, cancellationToken)
            : Task.FromResult(false);

        internal async Task<bool> ExportViewerPackageCoreAsync(string fileName,
            CancellationToken cancellationToken)
        {
            if (IsExporting || _grid == null) return false;
            RequireExportLease();
            string destination = ExportPathFor(fileName, ".zip");
            string exportDirectory = Path.GetDirectoryName(destination);
            string staging = Path.Combine(exportDirectory,
                Path.GetFileNameWithoutExtension(destination) + ".tmp");
            string temporaryArchive = destination + ".tmp";
            IsExporting = true;
            try
            {
                SetStatus("Exporting 3D Tiles…");
                cancellationToken.ThrowIfCancellationRequested();
                IProgress<OperationWorkProgress> progress =
                    new Progress<OperationWorkProgress>(value =>
                        _scanner?.ReportOperation(
                            ScanOperationKind.ExportGlb, value));
                MerkabaSpatialBinding spatialBinding =
                    await CaptureSpatialBindingAsync();
                cancellationToken.ThrowIfCancellationRequested();
                CaptureExportSource();
                MerkabaNativePackage.Source nativeSource = _persistence.CaptureExportPackageSource();
                using MerkabaNativePackage.Snapshot nativePackage = await Task.Run(() =>
                    MerkabaNativePackage.Capture(nativeSource, cancellationToken));
                byte[] viewerHtml = LoadViewerResource(ViewerResourceRoot);
                byte[] threeLicense = LoadViewerResource(
                    ViewerResourceRoot + "ThreeLicense");
                byte[] tilesLicense = LoadViewerResource(
                    ViewerResourceRoot + "TilesLicense");
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(exportDirectory);
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                    if (File.Exists(temporaryArchive))
                        File.Delete(temporaryArchive);
                });
                MerkabaTilesetResult result = await BuildStreamingTilesetAsync(
                    staging, spatialBinding, progress, cancellationToken, nativePackage);
                long archiveBytes = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.WriteAllBytes(Path.Combine(staging, "index.html"),
                        viewerHtml);
                    File.WriteAllBytes(Path.Combine(staging,
                            "THIRD_PARTY_THREE_LICENSE.txt"), threeLicense);
                    File.WriteAllBytes(Path.Combine(staging,
                            "THIRD_PARTY_3DTILESRENDERERJS_LICENSE.txt"),
                        tilesLicense);
                    long bytes = WriteViewerArchive(staging,
                        temporaryArchive, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    MerkabaFilePublishing.Publish(temporaryArchive,
                        destination);
                    return bytes;
                });
                LastExportPath = destination;
                _lastViewerPackagePath = destination;
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
                ReportExportFailure(exception);
                return false;
            }
            finally
            {
                CleanupExportStaging(temporaryArchive, staging);
                IsExporting = false;
                _exportTiles = null;
                _exportPosition = default;
                StatusChanged?.Invoke();
            }
        }

        public void ClearExport()
        {
            if (IsExporting || (_scanner?.ExportMutationHeld ?? false)) return;
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

        internal void ReportExportFailure(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                Logger.Info("Merkaba export cancelled after its current worker retired.");
                SetStatus("Export cancelled");
                return;
            }
            Logger.Error("Merkaba export failed: " + exception);
            SetStatus("Export failed: " + exception.Message);
        }

        private static void CleanupExportStaging(string file, string directory)
        {
            // Only this transaction's sanitized destination-derived staging.
            // Cleanup failure must not skip the finally that releases its lease.
            try
            {
                if (File.Exists(file)) File.Delete(file);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception exception)
            {
                Logger.Error("Could not remove export staging: " + exception.Message);
            }
        }

        private static void CheckpointGlb(MerkabaExportJournal journal,
            MerkabaGlbWriter.StreamingSession stream, ExportMetrics metrics,
            int stage, int nextTile, CancellationToken cancellationToken,
            long? nextDirtPacket = null)
        {
            // Advance a detached cursor only after every corresponding spool
            // range and atlas cell is durable. The previous receipt remains
            // authoritative if baking, writing or hashing is interrupted.
            var state = new MerkabaExportJournal.State
            {
                stage = stage, nextTile = nextTile,
                nextDirtPacket = nextDirtPacket ?? journal.Current.nextDirtPacket,
                occupied = metrics.CanonicalOccupiedCount,
                measured = metrics.MeasuredPatchCount,
                completed = metrics.InferredPatchCount,
                unresolved = metrics.UnresolvedMeasuredPlaneCount,
                dirtTriangles = metrics.DirtTriangles,
                status = "written", reason = "",
                glb = stream.Checkpoint(cancellationToken)
            };
            journal.Commit(state, MerkabaGlbWriter.JournalFiles, null, cancellationToken);
        }

        private static Task RecordExportFailureAsync(MerkabaExportJournal journal,
            Exception exception) => journal == null ? Task.CompletedTask : Task.Run(() =>
        {
            try { journal.NoteFailure(exception); }
            catch (Exception receiptFailure)
            {
                // Do not replace the original error or release the held source
                // while a detached failure-receipt worker is still running.
                Logger.Error("Could not record export failure: " + receiptFailure.Message);
            }
        });

        private void RequireExportLease()
        {
            if (_scanner == null || !_scanner.ExportMutationHeld ||
                _scanner.IsScanning || _scanner.IsScanStarting ||
                (_integrator != null && (_integrator.HasPendingObservation ||
                    _integrator.HasAttemptInFlight || _integrator.HasPendingFineErase ||
                    _integrator.HasFineEraseAttemptInFlight)) ||
                (_grid != null && _grid.HasObservationDurableCut))
                throw new InvalidOperationException(
                    "Export requires the held, quiesced and durable RoomScanner transaction.");
        }

        private static string ExportPathFor(string requested, string extension) =>
            Path.Combine(Application.persistentDataPath, "MerkabaScan", "exports",
                SanitizeExportFileName(requested, extension));

        // A name is one bounded filename, never an application-relative path.
        // Both export buttons share the same stem and supply their own suffix.
        internal static string SanitizeExportFileName(string requested,
            string extension)
        {
            string value = string.IsNullOrWhiteSpace(requested)
                ? ViewerPackageName : requested.Trim();
            if (value.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 4);
            int length = Math.Min(value.Length, 64);
            if (length > 0 && char.IsHighSurrogate(value[length - 1])) --length;
            var clean = new char[length];
            for (int index = 0; index < length; ++index)
            {
                char c = value[index];
                clean[index] = char.IsControl(c) || c == '/' || c == '\\' ||
                    c == ':' || c == '*' || c == '?' || c == '"' ||
                    c == '<' || c == '>' || c == '|' ? '-' : c;
            }
            string stem = new string(clean).Trim(' ', '.', '-');
            if (stem.Length == 0) stem = ViewerPackageName;
            return stem + extension;
        }

        private async Task<MerkabaTilesetResult> BuildStreamingTilesetAsync(
            string staging, MerkabaSpatialBinding spatialBinding,
            IProgress<OperationWorkProgress> progress,
            CancellationToken cancellationToken,
            MerkabaNativePackage.Snapshot nativePackage)
        {
            MerkabaTilesetWriter.BeginStreamingPackage(staging, cancellationToken);
            var leaves = new List<MerkabaTilesetLeaf>();
            await StreamOwnedFlowersAsync(async (owned, groupIndex,
                groupCount) =>
            {
                int leafIndex = leaves.Count;
                MerkabaTilesetLeaf leaf = await Task.Run(() =>
                    MerkabaTilesetWriter.WriteStreamingLeaf(staging,
                        leafIndex, owned, progress, cancellationToken: cancellationToken));
                leaves.Add(leaf);
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.WritingFile, groupIndex + 1, groupCount,
                    $"Streamed spatial leaf {groupIndex + 1}/{groupCount}"));
            }, progress, true, cancellationToken);
            await AppendDirtToTilesetAsync(staging, leaves, progress, cancellationToken);
            return await Task.Run(() =>
                MerkabaTilesetWriter.CompleteStreamingPackage(staging,
                    leaves, spatialBinding, cancellationToken, nativePackage));
        }

        // The same frozen source and shared direct/dual evaluator supplies
        // coverage. No caller can substitute an owner box or an all-uncovered
        // shortcut beside the final ordinary export path.
        internal Task<long> AppendDirtToGlbAsync(
            MerkabaGlbWriter.StreamingSession stream,
            IProgress<OperationWorkProgress> progress = null,
            CancellationToken cancellationToken = default) =>
            AppendDirtToGlbAsync(stream, progress, cancellationToken, null, null);

        private async Task<long> AppendDirtToGlbAsync(
            MerkabaGlbWriter.StreamingSession stream,
            IProgress<OperationWorkProgress> progress,
            CancellationToken cancellationToken, MerkabaExportJournal journal,
            ExportMetrics metrics)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            RequireQuiescedDirtExport();
            long ordinal = 0, completedPackets = journal?.Current.nextDirtPacket ?? 0;
            // The same bounded canonical DIRT traversal is replayed. Verified
            // packets are not appended again; no second geometry cursor or
            // world-sized list is introduced beside the shared evaluator.
            long trianglesWritten = await _grid.StreamStoredFlowerDirtAsync(
                _exportPlaneBounds, _exportTiles, _exportPosition, triangles =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long packet = ordinal++;
                    if (packet < completedPackets) return;
                    stream.AppendDirt(triangles, progress, cancellationToken: cancellationToken);
                    if (journal == null) return;
                    metrics.DirtTriangles = checked(metrics.DirtTriangles + triangles.Count);
                    CheckpointGlb(journal, stream, metrics, 1, _exportTiles.Length,
                        cancellationToken, ordinal);
                }, cancellationToken);
            if (journal != null && (ordinal < completedPackets ||
                journal.Current.dirtTriangles != trianglesWritten))
                throw new InvalidDataException("DIRT cursor differs from the unchanged committed source.");
            return trianglesWritten;
        }

        internal Task<long> AppendDirtToTilesetAsync(string staging,
            IList<MerkabaTilesetLeaf> leaves,
            IProgress<OperationWorkProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (leaves == null) throw new ArgumentNullException(nameof(leaves));
            RequireQuiescedDirtExport();
            return _grid.StreamStoredFlowerDirtAsync(_exportPlaneBounds, _exportTiles, _exportPosition, triangles =>
                leaves.Add(MerkabaTilesetWriter.WriteStreamingDirtLeaf(staging,
                    leaves.Count, triangles, progress,
                    cancellationToken: cancellationToken)), cancellationToken);
        }

        private float2 ExportPlaneBounds() => _depthCapture != null &&
            _depthCapture.TryGetFlowerPlaneBounds(out Vector2 bounds)
                ? new float2(bounds.x, bounds.y) : new float2(float.PositiveInfinity);

        private void CaptureExportSource()
        {
            RequireExportLease();
            _exportTiles = _grid.CaptureStoredFlowerSource(out _exportPosition);
            _exportPlaneBounds = ExportPlaneBounds();
        }

        private void RequireQuiescedDirtExport()
        {
            RequireExportLease();
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
            IProgress<OperationWorkProgress> progress, bool tilesetLeaves,
            CancellationToken cancellationToken, int startIndex = 0,
            Func<int, Task> completedTile = null)
        {
            MerkabaTileAddress[] addresses = _exportTiles ?? throw new InvalidOperationException("Flower export has no frozen source.");
            if (startIndex < 0 || startIndex > addresses.Length)
                throw new InvalidDataException("Export cursor is outside the frozen source index.");
            float2 planeBounds = _exportPlaneBounds;
            for (int index = startIndex; index < addresses.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reader = await _grid.ReadStoredFlowerContextAsync(addresses[index], addresses,
                        _exportPosition, cancellationToken)
                    .ConfigureAwait(false);
                MerkabaTileAddress address = addresses[index];
                MerkabaFlowerPresentation presentation = await Task.Run(() =>
                    MerkabaFlowerPresentation.Build(reader, address, planeBounds,
                        cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (presentation.TriangleCount != 0)
                    await consume(presentation, index, addresses.Length).ConfigureAwait(false);
                if (completedTile != null)
                    await completedTile(index + 1).ConfigureAwait(false);
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

            internal ExportMetrics(MerkabaExportJournal.State state = null)
            {
                if (state == null) return;
                CanonicalOccupiedCount = MeasuredPlaneOccupiedCount = state.occupied;
                MeasuredPatchCount = state.measured;
                InferredPatchCount = state.completed;
                UnresolvedMeasuredPlaneCount = state.unresolved;
                DirtTriangles = state.dirtTriangles;
            }

            internal void Add(MerkabaFlowerPresentation result)
            {
                CanonicalOccupiedCount += result.OccupiedOwners;
                MeasuredPlaneOccupiedCount += result.OccupiedOwners;
                UnresolvedMeasuredPlaneCount += result.UnresolvedWedges;
                foreach (var carrier in result.Carriers)
                {
                    uint completed = carrier.Symbol.CompletedWedgeMask;
                    MeasuredPatchCount += math.countbits(carrier.Symbol.ActiveWedgeMask & ~completed);
                    InferredPatchCount += math.countbits(completed);
                }
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
            string destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root = Path.GetFullPath(sourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string[] files = Directory.GetFiles(root, "*",
                SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            byte[] copyBuffer = new byte[64 * 1024];
            using (var stream = new FileStream(destination, FileMode.CreateNew,
                       FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
                       FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(stream,
                           ZipArchiveMode.Create, true))
                {
                    foreach (string file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
                        int count;
                        while ((count = input.Read(copyBuffer, 0, copyBuffer.Length)) != 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            output.Write(copyBuffer, 0, count);
                        }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(true);
                return stream.Length;
            }
        }

    }
}
