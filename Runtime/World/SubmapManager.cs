using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Optional large-world module. It observes the headset against the active local volume
    /// and finalizes/publishes source payloads outside the render-frame file-I/O path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SubmapManager : MonoBehaviour, IRoomScanModule
    {
        [Header("Large World")]
        [SerializeField] private bool largeWorldMode;
        [SerializeField, Min(2f), Tooltip("Storage/pose-graph extent of a canonical PRISM chunk; geometry itself is not voxelized")]
        private float prismChunkSizeMeters = 4f;
        [SerializeField] private SubmapRolloverSettings rollover = new();
        [SerializeField, Tooltip("Finalize and advance automatically at a chunk boundary")]
        private bool autoFinalizeRollover = true;
        [SerializeField, Min(0.25f)] private float finalizationRetrySeconds = 2f;
        [SerializeField, Min(1f), Tooltip("Interval for bounded world/residency telemetry used by the ADB performance harness")]
        private float worldProfilingIntervalSeconds = 5f;

        private RoomScanner _scanner;
        private WorldStore _store;
        private SubmapRolloverController _controller;
        private SubmapRolloverRequest _lastRaisedRequest;
        private KeyframeCollector _keyframes;
        private Task _finalizationTask;
        private Task _keyframeRestoreTask;
        private int _backgroundPublicationCount;
        private float _retryAfterUnscaledTime;
        private float _nextWorldProfileTime;
        private readonly Dictionary<string, Task> _chunkPublicationTails =
            new(StringComparer.Ordinal);

        public string ModuleName => "Infinite Submaps";
        public bool LargeWorldMode
        {
            get => largeWorldMode;
            set => largeWorldMode = value;
        }
        public bool HasWorld => _controller != null;
        internal WorldStore Store => _store;
        public WorldManifest Manifest => _controller?.Manifest;
        public ChunkRecord ActiveChunk => _controller?.ActiveChunk;
        public SubmapRolloverRequest PendingRequest => _controller?.PendingRequest;
        public int ResidentChunkCount =>
            _scanner?.PrismChunkResidency?.RecentCanonicalCount ?? 0;
        public float BoundaryMarginMeters => rollover.boundaryMarginMeters;
        public float OverlapMeters => rollover.overlapMeters;
        public float RearmHysteresisMeters => rollover.rearmHysteresisMeters;
        public bool UsesLargeWorldDefaults => rollover.UsesLargeWorldDefaults;
        public bool IsFinalizing => _finalizationTask != null && !_finalizationTask.IsCompleted;
        /// <summary>True while canonical PRISM arenas are staged or activated.</summary>
        public bool IsTransitioning => IsFinalizing ||
            (_scanner?.PrismChunkResidency?.IsTransitioning ?? false);
        public int BackgroundPublicationCount => _backgroundPublicationCount;
        public bool IsRestoring =>
            _keyframeRestoreTask != null && !_keyframeRestoreTask.IsCompleted;
        public string FinalizationStatus { get; private set; } = "Idle";

        public event Action<SubmapRolloverRequest> RolloverRequested;
        public event Action<ChunkRecord> ActiveChunkChanged;
        public event Action<string> FinalizationStatusChanged;
        public event Action<WorldStore, string, string, int> ChunkRevisionPublished;

        /// <summary>
        /// Waits until the active canonical PRISM generation and its immutable
        /// chunk artifact are stable enough to export. This is a publication
        /// barrier only; no legacy texture/server refinement is involved.
        /// </summary>
        internal async Task WaitForStablePublicationAsync()
        {
            Task finalization = _finalizationTask;
            if (finalization != null && !finalization.IsCompleted)
                await finalization;

            WorldManifest manifest = _controller?.Manifest;
            ChunkRecord chunk = _controller?.ActiveChunk;
            if (manifest == null || chunk == null)
                return;

            Task publication = GetChunkPublicationTail(manifest.worldId, chunk.chunkId);
            if (publication != null && !publication.IsCompleted)
                await publication;
        }

        /// <summary>
        /// Applies the documented Quest large-world baseline. It is deliberately idempotent
        /// and leaves the single-room path untouched when no SubmapManager is attached.
        /// </summary>
        public void ApplyLargeWorldDefaults()
        {
            rollover ??= new SubmapRolloverSettings();
            rollover.ApplyLargeWorldDefaults();
            LargeWorldMode = true;
        }

        public void OnModuleInitialize(RoomScanner scanner)
        {
            _scanner = scanner;
            _keyframes = scanner != null ? scanner.KeyframeCollector :
                GetComponent<KeyframeCollector>();
            if (_scanner != null)
            {
                _scanner.ScanAnchorCreated += OnScanAnchorCreated;
            }
        }

        private void OnDestroy()
        {
            if (_scanner != null)
            {
                _scanner.ScanAnchorCreated -= OnScanAnchorCreated;
            }
        }

        public void OnScanStarted()
        {
            if (!largeWorldMode)
                return;
            if (HasWorld)
            {
                if (_keyframeRestoreTask == null || _keyframeRestoreTask.IsCompleted)
                    ConfigureChunkKeyframes(_controller.ActiveChunk, true);
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = _keyframeRestoreTask == null ||
                                                _keyframeRestoreTask.IsCompleted;
                EmitWorldProfile("start");
                return;
            }
            Camera camera = Camera.main;
            if (camera == null)
            {
                Logger.Warning("Infinite Submaps: cannot create a world without a main camera.");
                return;
            }
            string worldId = "world-" + DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            if (!TryStartNewWorld(worldId, "Infinite scan",
                    new Pose(camera.transform.position, camera.transform.rotation), out string error))
                Logger.Error("Infinite Submaps: " + error);
        }

        public void OnScanStopped()
        {
            if (!largeWorldMode || _controller == null || _store == null ||
                _controller.PendingRequest != null || IsFinalizing || IsRestoring)
                return;
            _finalizationTask = FinalizeActivePrismChunkAsync();
        }

        public bool TryStartNewWorld(string worldId, string displayName, Pose cameraWorldPose,
            out string error)
        {
            error = null;
            float extent = Mathf.Max(1f, prismChunkSizeMeters * 0.5f);
            var extents = Vector3.one * extent;
            float yaw = cameraWorldPose.rotation.eulerAngles.y;
            var initialPose = new RigidPoseData(cameraWorldPose.position,
                Quaternion.Euler(0f, yaw, 0f));
            var bounds = new BoundsData(Vector3.zero, extents);
            string root = Path.Combine(Application.persistentDataPath, "InfiniteWorlds");
            var store = new WorldStore(root);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!WorldSessionFactory.TryCreate(store, worldId, displayName, initialPose,
                    bounds, now, out WorldManifest manifest, out error))
                return false;
            if (!SubmapRolloverController.TryCreate(manifest, rollover,
                    out SubmapRolloverController controller, out error))
                return false;
            _store = store;
            _controller = controller;
            _lastRaisedRequest = null;
            _keyframeRestoreTask = null;
            ConfigureChunkKeyframes(_controller.ActiveChunk, false);
            _scanner?.ConfigurePrismChunk(_controller.ActiveChunk);
            ActiveChunkChanged?.Invoke(_controller.ActiveChunk);
            Logger.Info($"Infinite world started: {worldId}, chunk={_controller.ActiveChunk.chunkId}, " +
                        $"bounds={bounds.extents * 2f}");
            EmitWorldProfile("start");
            return true;
        }

        public bool TryAttachWorld(WorldStore store, WorldManifest manifest, out string error)
        {
            error = null;
            if (store == null)
            {
                error = "World store is required.";
                return false;
            }
            if (!SubmapRolloverController.TryCreate(manifest, rollover,
                    out SubmapRolloverController controller, out error))
                return false;
            _store = store;
            _controller = controller;
            _lastRaisedRequest = null;
            _scanner?.ConfigurePrismChunk(_controller.ActiveChunk);
            ActiveChunkChanged?.Invoke(_controller.ActiveChunk);
            BeginRestoreChunkKeyframes(_controller.ActiveChunk);
            EmitWorldProfile("attach");
            return true;
        }

        /// <summary>
        /// Commits the next graph vertex after the source canonical snapshot is durable.
        /// </summary>
        public bool TryCompletePendingRollover(out string error)
        {
            return TryCompletePrismRollover(ChunkLifecycleState.Persisted,
                out _, out _, out error);
        }

        private bool TryCompletePrismRollover(ChunkLifecycleState sourceState,
            out ChunkRecord source, out ChunkRecord target, out string error)
        {
            source = null;
            target = null;
            error = null;
            if (_controller == null || _store == null)
            {
                error = "No active infinite world.";
                return false;
            }
            SubmapRolloverRequest pending = _controller.PendingRequest;
            source = pending != null ? FindChunk(pending.SourceChunkId) : null;
            if (source == null)
            {
                error = "Pending rollover source chunk is missing.";
                return false;
            }
            long now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _controller.Manifest.updatedUnixMilliseconds);
            if (!_controller.TryCommitPending(_store, now, sourceState,
                    out target, out error))
                return false;

            if (pending.IsRevisit)
                BeginRestoreChunkKeyframes(target);
            else
                ConfigureChunkKeyframes(target, false);
            _lastRaisedRequest = null;
            if (_keyframes != null)
                _keyframes.CaptureEnabled = !pending.IsRevisit ||
                    _keyframeRestoreTask == null || _keyframeRestoreTask.IsCompleted;
            _scanner?.ConfigurePrismChunk(target);
            ActiveChunkChanged?.Invoke(target);
            Logger.Info($"Cone-PRISM rollover complete: active={target.chunkId}, " +
                        $"revisit={pending.IsRevisit}");
            EmitWorldProfile("rollover");
            return true;
        }

        public void CancelPendingRollover()
        {
            _controller?.CancelPending();
            _lastRaisedRequest = null;
            if (_keyframes != null)
                _keyframes.CaptureEnabled = true;
        }

        private void Update()
        {
            if (!largeWorldMode || _controller == null || _scanner == null ||
                !_scanner.IsScanning)
                return;
            if (Time.unscaledTime >= _nextWorldProfileTime)
            {
                _nextWorldProfileTime = Time.unscaledTime +
                    Mathf.Max(1f, worldProfilingIntervalSeconds);
                EmitWorldProfile("periodic");
            }
            if (IsRestoring)
                return;
            Camera camera = Camera.main;
            if (camera == null)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!_controller.TryObserveCamera(camera.transform.position, now,
                    out SubmapRolloverRequest request, out string error))
            {
                if (!string.IsNullOrEmpty(error))
                    Logger.Warning("Submap observation rejected: " + error);
                return;
            }
            if (request != null && !ReferenceEquals(request, _lastRaisedRequest))
            {
                if (autoFinalizeRollover && Time.unscaledTime < _retryAfterUnscaledTime)
                    return;
                _lastRaisedRequest = request;
                RolloverRequested?.Invoke(request);
                Logger.Info($"Submap rollover requested: {request.SourceChunkId} -> " +
                            $"{request.TargetChunkId}, axis={request.BoundaryAxis}, " +
                            $"direction={request.BoundaryDirection}");
                if (autoFinalizeRollover && !IsFinalizing &&
                    Time.unscaledTime >= _retryAfterUnscaledTime)
                    _finalizationTask = FinalizePrismAndAdvanceAsync(request);
            }
        }

        private void EmitWorldProfile(string reason)
        {
            WorldManifest manifest = _controller?.Manifest;
            ChunkRecord active = _controller?.ActiveChunk;
            if (manifest == null || active == null)
                return;
            Logger.Info(InfiniteScanPerformanceTelemetry.Format(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), reason,
                manifest.chunks?.Count ?? 0, active.revision, active.state,
                manifest.edges?.Count ?? 0, 1, 1, ResidentChunkCount,
                0, BackgroundPublicationCount,
                Profiler.GetTotalAllocatedMemoryLong(),
                Profiler.GetTotalReservedMemoryLong()));
        }

        private async Task FinalizePrismAndAdvanceAsync(SubmapRolloverRequest request)
        {
            SetFinalizationStatus("Staging canonical PRISM chunk");
            try
            {
                if (_controller == null || _store == null || request == null ||
                    !ReferenceEquals(request, _controller.PendingRequest))
                    throw new InvalidOperationException(
                        "Rollover request is no longer active.");
                PrismChunkResidencyManager residency = _scanner?.PrismChunkResidency;
                if (residency == null)
                    throw new InvalidOperationException(
                        "PRISM chunk residency manager is unavailable.");
                ChunkRecord requestedSource = FindChunk(request.SourceChunkId);
                if (requestedSource == null)
                    throw new InvalidOperationException(
                        "Source PRISM chunk is missing from the world.");
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = false;
                if (_keyframes != null &&
                    !await _keyframes.WaitForPendingWritesAsync())
                    throw new IOException(
                        "Timed out waiting for active keyframe writes.");

                PrismCanonicalChunkSnapshot snapshot =
                    await residency.StageChunkAsync(request.SourceChunkId);
                SetFinalizationStatus("Switching canonical PRISM chunk");
                if (!TryCompletePrismRollover(ChunkLifecycleState.Finalizing,
                        out ChunkRecord source, out ChunkRecord target,
                        out string commitError))
                    throw new InvalidOperationException(commitError);

                QueuePrismPublication(source, snapshot);
                long elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() -
                               request.RequestedUnixMilliseconds;
                Logger.Info($"Cone-PRISM live switch: {source.chunkId} -> " +
                            $"{target.chunkId} in {elapsed}ms; canonical persistence " +
                            "continues in background");
            }
            catch (Exception exception)
            {
                SetFinalizationStatus("Failed: " + exception.Message);
                Logger.Error("Cone-PRISM rollover failed: " + exception);
                _controller?.CancelPending();
                _lastRaisedRequest = null;
                _retryAfterUnscaledTime = Time.unscaledTime +
                    Mathf.Max(0.25f, finalizationRetrySeconds);
            }
            finally
            {
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = true;
                _finalizationTask = null;
            }
        }

        private async Task FinalizeActivePrismChunkAsync()
        {
            SetFinalizationStatus("Saving canonical PRISM chunk");
            try
            {
                ChunkRecord chunk = _controller?.ActiveChunk;
                PrismChunkResidencyManager residency = _scanner?.PrismChunkResidency;
                if (chunk == null || _store == null || residency == null)
                    throw new InvalidOperationException(
                        "No active PRISM chunk can be finalized.");
                Task pending = GetChunkPublicationTail(_controller.Manifest.worldId,
                    chunk.chunkId);
                if (pending != null && !pending.IsCompleted)
                    await pending;
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = false;
                if (_keyframes != null &&
                    !await _keyframes.WaitForPendingWritesAsync())
                    throw new IOException(
                        "Timed out waiting for active keyframe writes.");
                PrismCanonicalChunkSnapshot snapshot =
                    await residency.StageChunkAsync(chunk.chunkId);
                PrismChunkPublishResult publication =
                    await residency.PublishChunkAsync(chunk, snapshot);
                if (!publication.Success)
                    throw new IOException(publication.Error);
                ChunkRevisionPublished?.Invoke(_store, _controller.Manifest.worldId,
                    chunk.chunkId, publication.Revision);
                SetFinalizationStatus("Idle");
            }
            catch (Exception exception)
            {
                SetFinalizationStatus("Failed: " + exception.Message);
                Logger.Error("Active Cone-PRISM finalization failed: " + exception);
            }
            finally
            {
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = true;
                _finalizationTask = null;
            }
        }

        private void QueuePrismPublication(ChunkRecord source,
            PrismCanonicalChunkSnapshot snapshot)
        {
            if (source == null || snapshot == null || _controller?.Manifest == null)
                return;
            string publicationKey = PublicationKey(_controller.Manifest.worldId,
                source.chunkId);
            _chunkPublicationTails.TryGetValue(publicationKey, out Task previous);
            _backgroundPublicationCount++;
            SetFinalizationStatus($"Scanning; persisting " +
                                  $"{_backgroundPublicationCount} chunk(s)");
            Task tail = PersistPrismChunkAfterAsync(previous, source, snapshot);
            _chunkPublicationTails[publicationKey] = tail;
        }

        private async Task PersistPrismChunkAfterAsync(Task previous, ChunkRecord source,
            PrismCanonicalChunkSnapshot snapshot)
        {
            try
            {
                if (previous != null)
                    await previous;
                PrismChunkResidencyManager residency = _scanner?.PrismChunkResidency;
                if (residency == null)
                    throw new InvalidOperationException(
                        "PRISM residency manager disappeared during publication.");
                PrismChunkPublishResult result = await residency.PublishChunkAsync(
                    source, snapshot, ChunkLifecycleState.Persisted);
                if (!result.Success)
                    throw new IOException(result.Error);
                ChunkRevisionPublished?.Invoke(_store, _controller.Manifest.worldId,
                    source.chunkId, result.Revision);
            }
            catch (Exception exception)
            {
                Logger.Error($"Canonical PRISM publication failed for " +
                             $"{source?.chunkId}: {exception}");
                SetFinalizationStatus("Background persistence failed: " +
                                      exception.Message);
            }
            finally
            {
                _backgroundPublicationCount = Math.Max(0,
                    _backgroundPublicationCount - 1);
                if (_backgroundPublicationCount == 0 &&
                    !FinalizationStatus.StartsWith(
                        "Background persistence failed", StringComparison.Ordinal))
                    SetFinalizationStatus("Idle");
            }
        }

        // Retained only for importing historical QRS volume packages. The live scanner
        // never calls this legacy path.
        private Task GetChunkPublicationTail(string worldId, string chunkId)
        {
            return _chunkPublicationTails.TryGetValue(PublicationKey(worldId, chunkId),
                out Task task) ? task : null;
        }

        private static string PublicationKey(string worldId, string chunkId)
        {
            return (worldId ?? string.Empty) + "/" + (chunkId ?? string.Empty);
        }

        private ChunkRecord FindChunk(string chunkId)
        {
            return _controller?.Manifest?.chunks?.Find(candidate =>
                candidate != null && string.Equals(candidate.chunkId, chunkId,
                    StringComparison.Ordinal));
        }

        private void ConfigureChunkKeyframes(ChunkRecord chunk, bool appendExisting)
        {
            if (_keyframes == null || _store == null || _controller?.Manifest == null ||
                chunk == null)
                return;
            string directory = Path.Combine(_store.GetChunkWorkingDirectory(
                _controller.Manifest.worldId, chunk.chunkId), "keyframes");
            _keyframes.SetChunkContext(directory, chunk.chunkId, chunk.revision,
                chunk.worldFromChunk, appendExisting);
        }

        private void OnScanAnchorCreated(Guid uuid, Matrix4x4 _)
        {
            if (_store == null || _controller?.Manifest == null || uuid == Guid.Empty)
                return;
            WorldManifest manifest = _controller.Manifest;
            string anchorId = uuid.ToString("D");
            if (string.Equals(manifest.worldAnchorId, anchorId, StringComparison.Ordinal))
                return;

            string previousWorldAnchor = manifest.worldAnchorId;
            int previousRevision = manifest.revision;
            long previousUpdated = manifest.updatedUnixMilliseconds;
            var previousChunkAnchors = new string[manifest.chunks.Count];
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                previousChunkAnchors[i] = manifest.chunks[i].anchorId;
                manifest.chunks[i].anchorId = anchorId;
            }
            manifest.worldAnchorId = anchorId;
            manifest.revision++;
            manifest.updatedUnixMilliseconds = Math.Max(previousUpdated,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (_store.TryCommitManifest(manifest, out string error))
            {
                Logger.Info($"Infinite world linked to spatial anchor {anchorId}");
                return;
            }

            manifest.worldAnchorId = previousWorldAnchor;
            manifest.revision = previousRevision;
            manifest.updatedUnixMilliseconds = previousUpdated;
            for (int i = 0; i < manifest.chunks.Count; i++)
                manifest.chunks[i].anchorId = previousChunkAnchors[i];
            Logger.Error("Infinite world anchor commit failed: " + error);
        }

        private void BeginRestoreChunkKeyframes(ChunkRecord chunk)
        {
            if (_keyframes == null || _store == null || _controller?.Manifest == null ||
                chunk == null)
                return;
            string directory = GetChunkKeyframeDirectory(chunk);
            if (File.Exists(Path.Combine(directory, "frames.jsonl")))
            {
                ConfigureChunkKeyframes(chunk, true);
                _keyframes.CaptureEnabled = true;
                _keyframeRestoreTask = null;
                return;
            }

            ChunkArtifactRecord artifact = chunk.artifacts?.Find(candidate =>
                candidate != null && candidate.kind == ChunkArtifactKind.Keyframes);
            if (artifact == null)
            {
                ConfigureChunkKeyframes(chunk, false);
                _keyframes.CaptureEnabled = true;
                _keyframeRestoreTask = null;
                return;
            }

            _keyframes.CaptureEnabled = false;
            _keyframes.SetExportDirectory(null);
            _keyframeRestoreTask = RestoreChunkKeyframesAsync(chunk, artifact, directory);
        }

        private async Task RestoreChunkKeyframesAsync(ChunkRecord chunk,
            ChunkArtifactRecord artifact, string destinationDirectory)
        {
            string worldId = _controller.Manifest.worldId;
            KeyframeRestoreResult result = await Task.Run(() => RestoreKeyframeArtifact(
                _store, worldId, artifact, destinationDirectory));
            if (_controller == null || !ReferenceEquals(chunk, _controller.ActiveChunk))
                return;
            if (!result.Success)
            {
                Logger.Error($"Chunk {chunk.chunkId} keyframe restore failed: {result.Error}");
                ConfigureChunkKeyframes(chunk, false);
            }
            else
            {
                ConfigureChunkKeyframes(chunk, true);
                if (!string.IsNullOrEmpty(result.QuarantinedDirectory))
                    Logger.Warning("Preserved incomplete keyframe workspace at " +
                                   result.QuarantinedDirectory);
                Logger.Info($"Restored chunk {chunk.chunkId} keyframes from revision " +
                            artifact.chunkRevision);
            }
            _keyframes.CaptureEnabled = true;
        }

        private string GetChunkKeyframeDirectory(ChunkRecord chunk)
        {
            return Path.Combine(_store.GetChunkWorkingDirectory(
                _controller.Manifest.worldId, chunk.chunkId), "keyframes");
        }

        private void SetFinalizationStatus(string status)
        {
            FinalizationStatus = status ?? string.Empty;
            FinalizationStatusChanged?.Invoke(FinalizationStatus);
        }

        private static KeyframeRestoreResult RestoreKeyframeArtifact(WorldStore store,
            string worldId, ChunkArtifactRecord artifact, string destinationDirectory)
        {
            string quarantine = null;
            try
            {
                if (Directory.Exists(destinationDirectory))
                {
                    quarantine = destinationDirectory + ".incomplete-" +
                                 Guid.NewGuid().ToString("N");
                    Directory.Move(destinationDirectory, quarantine);
                }
                if (!store.TryResolveVerifiedArtifact(worldId, artifact, out string path,
                        out string error))
                    return new KeyframeRestoreResult
                    {
                        Error = error,
                        QuarantinedDirectory = quarantine
                    };
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                if (!ChunkKeyframeArchive.TryExtract(stream, destinationDirectory, out error))
                    return new KeyframeRestoreResult
                    {
                        Error = error,
                        QuarantinedDirectory = quarantine
                    };
                return new KeyframeRestoreResult
                {
                    Success = true,
                    QuarantinedDirectory = quarantine
                };
            }
            catch (Exception exception)
            {
                return new KeyframeRestoreResult
                {
                    Error = "Keyframe artifact restore failed: " + exception.Message,
                    QuarantinedDirectory = quarantine
                };
            }
        }

        private sealed class KeyframeRestoreResult
        {
            public bool Success;
            public string Error;
            public string QuarantinedDirectory;
        }
    }
}
