using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan.Exporting
{
    public sealed class GlbUserExportResult
    {
        public bool Success => string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(Path);
        public string Path { get; internal set; }
        public string Error { get; internal set; }
        public WorldGlbExportResult World { get; internal set; }
    }

    /// <summary>Operator-facing, scan-frame-independent GLB export lifecycle.</summary>
    [DisallowMultipleComponent]
    public sealed class GlbExportController : MonoBehaviour, IRoomScanModule
    {
        [SerializeField, Range(0f, 1f)] private float roughnessFactor = 0.8f;
        [SerializeField, Range(0f, 16f)] private float normalScale = 1f;
        [SerializeField] private bool writeMonolithicWorldGlb = true;
        [SerializeField, Min(64)] private int maximumMonolithicMiB = 2048;

        private RoomScanner _scanner;
        private SubmapManager _submaps;
        private CancellationTokenSource _lifetime;

        public string ModuleName => "GLB/PBR Export";
        public bool IsBusy { get; private set; }
        public string Status { get; private set; } = "Idle";
        public string LastExportPath { get; private set; }
        public string ExportRoot => Path.Combine(Application.persistentDataPath, "Exports");

        public event Action<string> StatusChanged;

        public void OnModuleInitialize(RoomScanner scanner)
        {
            _scanner = scanner;
            _submaps = scanner != null ? scanner.GetComponent<SubmapManager>() :
                GetComponent<SubmapManager>();
            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
        }

        private void OnDestroy()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
        }

        public async Task<GlbUserExportResult> ExportActiveChunkAsync()
        {
            if (!TryBegin("Exporting active chunk", out GlbUserExportResult failed))
                return failed;
            try
            {
                await _submaps.WaitForStablePublicationAsync();
                WorldManifest manifest = _submaps.Manifest;
                ChunkRecord chunk = _submaps.ActiveChunk;
                if (manifest == null || chunk == null || _submaps.Store == null)
                    return Fail("No active infinite-world chunk is available.");
                int revision = chunk.revision;
                long now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Math.Max(manifest.updatedUnixMilliseconds,
                        chunk.updatedUnixMilliseconds));
                ChunkGlbExportResult exported = await ChunkGlbExporter.ExportRefinedAsync(
                    _submaps.Store, manifest, chunk, MaterialOptions(), now,
                    _lifetime.Token);
                if (!exported.Success)
                    return Fail(exported.Error);
                if (chunk.revision != revision)
                    return Fail("Active chunk revision changed during export.");
                if (!_submaps.Store.TryResolveVerifiedArtifact(manifest.worldId,
                        exported.Artifact, out string sourcePath, out string verifyError))
                    return Fail(verifyError);

                string directory = Path.Combine(ExportRoot, manifest.worldId, "chunks");
                string fileName = chunk.chunkId + "_r" + revision.ToString("D10",
                    CultureInfo.InvariantCulture) + "_" +
                    exported.Artifact.sha256.Substring(0, 12) + ".glb";
                string destination = Path.Combine(directory, fileName);
                string copyError = await Task.Run(() => CopyImmutable(sourcePath,
                    destination, exported.Artifact, _lifetime.Token), _lifetime.Token);
                if (copyError != null)
                    return Fail(copyError);
                return Succeed(destination);
            }
            catch (OperationCanceledException)
            {
                return Fail("Chunk GLB export was canceled.");
            }
            finally
            {
                End();
            }
        }

        public async Task<GlbUserExportResult> ExportWorldAsync()
        {
            if (!TryBegin("Exporting world", out GlbUserExportResult failed))
                return failed;
            try
            {
                await _submaps.WaitForStablePublicationAsync();
                WorldManifest manifest = _submaps.Manifest;
                if (manifest == null || _submaps.Store == null)
                    return Fail("No infinite world is available.");
                int graphRevision = manifest.revision;
                string suffix = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff",
                    CultureInfo.InvariantCulture);
                string directory = Path.Combine(ExportRoot,
                    manifest.worldId + "_r" + graphRevision.ToString("D10",
                        CultureInfo.InvariantCulture) + "_" + suffix);
                long now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    manifest.updatedUnixMilliseconds);
                WorldGlbExportResult exported = await WorldGlbExporter.ExportAsync(
                    _submaps.Store, manifest, directory, new WorldGlbExportOptions
                    {
                        Material = MaterialOptions(),
                        WriteMonolithicGlb = writeMonolithicWorldGlb,
                        MaximumMonolithicByteLength = (long)maximumMonolithicMiB *
                                                      1024L * 1024L
                    }, now, _lifetime.Token);
                if (!exported.Success)
                    return Fail(exported.Error);
                string message = exported.MonolithicGlbPath != null
                    ? exported.MonolithicGlbPath
                    : exported.BuildingManifestPath;
                GlbUserExportResult success = Succeed(message);
                success.World = exported;
                return success;
            }
            catch (OperationCanceledException)
            {
                return Fail("World GLB export was canceled.");
            }
            finally
            {
                End();
            }
        }

        private bool TryBegin(string status, out GlbUserExportResult failure)
        {
            failure = null;
            if (IsBusy)
            {
                failure = new GlbUserExportResult
                {
                    Error = "Another GLB export is already running."
                };
                return false;
            }
            if (_scanner == null || _submaps == null || !_submaps.LargeWorldMode)
            {
                failure = new GlbUserExportResult
                {
                    Error = "Infinite-world GLB export is not configured."
                };
                return false;
            }
            if (_scanner.IsScanning)
            {
                failure = new GlbUserExportResult
                {
                    Error = "Stop scanning before GLB export so revisions stay immutable."
                };
                return false;
            }
            IsBusy = true;
            SetStatus(status);
            return true;
        }

        private ChunkGlbWriteOptions MaterialOptions() => new()
        {
            RoughnessFactor = roughnessFactor,
            NormalScale = normalScale,
            DoubleSided = true
        };

        private GlbUserExportResult Succeed(string path)
        {
            LastExportPath = path;
            SetStatus("Complete: " + path);
            return new GlbUserExportResult { Path = path };
        }

        private GlbUserExportResult Fail(string error)
        {
            string message = string.IsNullOrEmpty(error) ? "GLB export failed." : error;
            SetStatus("Failed: " + message);
            return new GlbUserExportResult { Error = message };
        }

        private void End()
        {
            IsBusy = false;
        }

        private void SetStatus(string status)
        {
            Status = status ?? string.Empty;
            StatusChanged?.Invoke(Status);
        }

        private static string CopyImmutable(string sourcePath, string destinationPath,
            ChunkArtifactRecord artifact, CancellationToken cancellationToken)
        {
            string pending = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                if (File.Exists(destinationPath))
                {
                    return new FileInfo(destinationPath).Length == artifact.byteLength &&
                           string.Equals(Hashing.ComputeSha256(destinationPath),
                               artifact.sha256, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : "Existing content-addressed GLB export is inconsistent.";
                }
                pending = destinationPath + ".pending-" + Guid.NewGuid().ToString("N");
                using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                           FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
                using (var destination = new FileStream(pending, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None, 1024 * 1024,
                           FileOptions.WriteThrough))
                {
                    var buffer = new byte[1024 * 1024];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int read = source.Read(buffer, 0, buffer.Length);
                        if (read == 0) break;
                        destination.Write(buffer, 0, read);
                    }
                    destination.Flush(true);
                }
                if (new FileInfo(pending).Length != artifact.byteLength ||
                    !string.Equals(Hashing.ComputeSha256(pending), artifact.sha256,
                        StringComparison.OrdinalIgnoreCase))
                    return "Copied GLB failed hash/length verification.";
                File.Move(pending, destinationPath);
                pending = null;
                return null;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is InvalidDataException)
            {
                return "GLB export copy failed: " + exception.Message;
            }
            finally
            {
                try { if (pending != null && File.Exists(pending)) File.Delete(pending); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
