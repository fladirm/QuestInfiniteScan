using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan.HeavyCompute
{
    public enum HeavyComputeBackendMode
    {
        None = 0,
        Lan = 1
    }

    /// <summary>
    /// Unity lifecycle adapter for the durable job scheduler. Chunk publication merely
    /// starts a worker-side bundle task; all HTTP operations are pumped independently from
    /// the scanner's integration callback and may remain offline indefinitely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChunkRefinementScheduler : MonoBehaviour, IRoomScanModule
    {
        [SerializeField] private HeavyComputeBackendMode backendMode =
            HeavyComputeBackendMode.None;
        [SerializeField] private string serverUrl = "http://127.0.0.1:8000";
        [SerializeField] private string profile = "balanced";
        [SerializeField] private bool allowFreshFallback = true;
        [SerializeField] private bool autoQueuePersistedChunks = true;
        [SerializeField, Min(0.1f)] private float pumpIntervalSeconds = 0.5f;
        [SerializeField, Min(5)] private int requestTimeoutSeconds = 60;

        private SubmapManager _submaps;
        private WorldStore _worldStore;
        private HeavyComputeQueueStore _queue;
        private IHeavyComputeBackend _backend;
        private HeavyComputeJobScheduler _scheduler;
        private CancellationTokenSource _lifetime;
        private Task _pumpTask;
        private Task _promotionTask;
        private float _nextPumpTime;
        private readonly HashSet<string> _bundleTasks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _handledReadyJobs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _promotionRetryUnixMs =
            new(StringComparer.Ordinal);

        public string ModuleName => "Chunk Refinement Scheduler";
        public HeavyComputeBackendMode BackendMode => backendMode;
        public string ServerUrl => serverUrl;
        public string Profile => profile;
        public string BackendName => _backend?.Name ?? "none";
        public bool IsPumping => _pumpTask != null && !_pumpTask.IsCompleted;
        public bool IsPromoting => _promotionTask != null && !_promotionTask.IsCompleted;
        public IReadOnlyList<HeavyComputeQueueItem> Jobs =>
            _queue?.Snapshot() ?? Array.Empty<HeavyComputeQueueItem>();

        public event Action QueueChanged;
        /// <summary>Raised only after the downloaded bundle is validated and atomically persisted.</summary>
        public event Action<HeavyComputeQueueItem> ArtifactReady;
        public event Action<HeavyComputeQueueItem, DiffSoupArtifactPublishResult>
            ArtifactPromoted;

        /// <summary>
        /// Configures the serialized backend before Unity initializes scanner modules.
        /// Setup/build automation uses this instead of reaching into private serialized
        /// fields, and fails closed when a LAN endpoint is malformed.
        /// </summary>
        internal bool TryConfigureBeforeInitialization(HeavyComputeBackendMode mode,
            string configuredServerUrl, out string error)
        {
            return TryConfigureBeforeInitialization(mode, configuredServerUrl, profile,
                out error);
        }

        internal bool TryConfigureBeforeInitialization(HeavyComputeBackendMode mode,
            string configuredServerUrl, string configuredProfile, out string error)
        {
            error = null;
            if (_scheduler != null)
            {
                error = "The refinement backend cannot be reconfigured after initialization.";
                return false;
            }

            string normalizedProfile = configuredProfile?.Trim().ToLowerInvariant();
            if (normalizedProfile != "preview" && normalizedProfile != "balanced" &&
                normalizedProfile != "quality")
            {
                error = "DiffSoup profile must be preview, balanced, or quality.";
                return false;
            }

            if (mode == HeavyComputeBackendMode.None)
            {
                backendMode = HeavyComputeBackendMode.None;
                profile = normalizedProfile;
                return true;
            }

            if (mode != HeavyComputeBackendMode.Lan ||
                !LanDiffSoupBackend.TryNormalizeBaseUri(configuredServerUrl,
                    out Uri normalized, out error))
                return false;

            backendMode = HeavyComputeBackendMode.Lan;
            serverUrl = normalized.AbsoluteUri.TrimEnd('/');
            profile = normalizedProfile;
            return true;
        }

        public void OnModuleInitialize(RoomScanner scanner)
        {
            _submaps = scanner != null ? scanner.GetComponent<SubmapManager>() :
                GetComponent<SubmapManager>();
            if (_submaps != null)
                _submaps.ChunkRevisionPublished += OnChunkRevisionPublished;
            _lifetime = new CancellationTokenSource();
            TryInitialize(Application.persistentDataPath, out string error);
            if (!string.IsNullOrEmpty(error))
                Logger.Error("Chunk refinement scheduler disabled: " + error);
        }

        private void OnDestroy()
        {
            if (_submaps != null)
                _submaps.ChunkRevisionPublished -= OnChunkRevisionPublished;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
        }

        private void Update()
        {
            if (_scheduler == null)
                return;
            if (_promotionTask != null && _promotionTask.IsCompleted)
                _promotionTask = null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_promotionTask == null && TryFindReadyForPromotion(now,
                    out HeavyComputeQueueItem ready))
                _promotionTask = PromoteAndNotifyAsync(ready, _lifetime.Token);

            if (!_backend.IsEnabled || IsPumping ||
                Time.unscaledTime < _nextPumpTime)
                return;
            _nextPumpTime = Time.unscaledTime + Mathf.Max(0.1f, pumpIntervalSeconds);
            _pumpTask = PumpAndNotifyAsync(_lifetime.Token);
        }

        public async Task<HeavyComputeQueueItem> QueueChunkRevisionAsync(WorldStore store,
            string worldId, string chunkId, int chunkRevision,
            HeavyComputeWarmStart warmStart = null)
        {
            if (_queue == null || store == null)
                return null;
            var key = new HeavyComputeJobKey(worldId, chunkId, chunkRevision);
            string jobId;
            try { jobId = key.JobId; }
            catch (ArgumentException exception)
            {
                Logger.Error("Chunk refinement queue rejected: " + exception.Message);
                return null;
            }
            lock (_bundleTasks)
            {
                if (!_bundleTasks.Add(jobId))
                    return FindJob(jobId);
            }
            try
            {
                string destination = _queue.GetInputPath(jobId);
                ChunkBundleBuildResult built = await Task.Run(() => ChunkBundleBuilder.Build(
                    store, worldId, chunkId, chunkRevision, destination));
                if (!built.Success)
                {
                    Logger.Error($"Chunk {chunkId} revision {chunkRevision} bundle failed: " +
                                 built.Error);
                    return null;
                }
                if (!HeavyComputeSubmission.TryCreate(key, built.Descriptor, profile,
                        allowFreshFallback, warmStart, out HeavyComputeSubmission submission,
                        out string submissionError))
                {
                    Logger.Error("Chunk refinement submission rejected: " + submissionError);
                    return null;
                }
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (!_queue.TryEnqueue(submission, built.BundlePath, now,
                        out HeavyComputeQueueItem queued, out string queueError))
                {
                    Logger.Error("Chunk refinement queue commit failed: " + queueError);
                    return null;
                }
                QueueChanged?.Invoke();
                return queued;
            }
            finally
            {
                lock (_bundleTasks) _bundleTasks.Remove(jobId);
            }
        }

        public bool TryCancel(string jobId, out string error)
        {
            if (_queue == null)
            {
                error = "Refinement queue is unavailable.";
                return false;
            }
            bool result = _queue.TryCancel(jobId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out error);
            if (result) QueueChanged?.Invoke();
            return result;
        }

        public bool TryRetry(string jobId, out string error)
        {
            if (_queue == null)
            {
                error = "Refinement queue is unavailable.";
                return false;
            }
            bool result = _queue.TryRetry(jobId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out error);
            if (result) QueueChanged?.Invoke();
            return result;
        }

        internal bool TryInitialize(string persistentDataPath, out string error)
        {
            error = null;
            try
            {
                string worldsRoot = Path.Combine(Path.GetFullPath(persistentDataPath),
                    "InfiniteWorlds");
                _worldStore = new WorldStore(worldsRoot);
                _queue = new HeavyComputeQueueStore(Path.Combine(worldsRoot,
                    ".heavy-compute"));
                _backend = backendMode == HeavyComputeBackendMode.Lan
                    ? new LanDiffSoupBackend(serverUrl, requestTimeoutSeconds)
                    : new NoneHeavyComputeBackend();
                _scheduler = new HeavyComputeJobScheduler(_queue, _backend,
                    IsCurrentRevision);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                _queue = null;
                _backend = new NoneHeavyComputeBackend();
                _scheduler = null;
                return false;
            }
        }

        private async Task PumpAndNotifyAsync(CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<HeavyComputeQueueItem> before = _queue.Snapshot();
                bool worked = await _scheduler.PumpOnceAsync(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cancellationToken);
                if (!worked) return;
                IReadOnlyList<HeavyComputeQueueItem> after = _queue.Snapshot();
                QueueChanged?.Invoke();
                for (int i = 0; i < after.Count; i++)
                {
                    if (after[i].localState != HeavyComputeLocalState.Ready)
                        continue;
                    HeavyComputeQueueItem prior = Find(before, after[i].JobId);
                    if (prior == null || prior.localState != HeavyComputeLocalState.Ready)
                    {
                        // Promotion is reconciled by Update. Keeping it separate from the
                        // network pump makes a downloaded job recoverable after app restart.
                        _promotionRetryUnixMs.Remove(after[i].JobId);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Logger.Error("Chunk refinement pump failed: " + exception);
            }
        }

        private void OnChunkRevisionPublished(WorldStore store, string worldId,
            string chunkId, int chunkRevision)
        {
            if (!autoQueuePersistedChunks || backendMode != HeavyComputeBackendMode.Lan)
                return;
            _ = QueueChunkRevisionAsync(store, worldId, chunkId, chunkRevision);
        }

        private bool IsCurrentRevision(string worldId, string chunkId, int revision)
        {
            if (_worldStore == null || !_worldStore.TryLoadManifest(worldId,
                    out WorldManifest manifest, out _, out _))
                return true; // Never discard solely because storage is temporarily unreadable.
            ChunkRecord chunk = manifest.chunks.Find(candidate => candidate != null &&
                string.Equals(candidate.chunkId, chunkId, StringComparison.Ordinal));
            return chunk != null && chunk.revision == revision;
        }

        private bool TryFindReadyForPromotion(long unixMilliseconds,
            out HeavyComputeQueueItem ready)
        {
            ready = null;
            IReadOnlyList<HeavyComputeQueueItem> jobs = _queue?.Snapshot();
            if (jobs == null) return false;
            for (int i = 0; i < jobs.Count; i++)
            {
                HeavyComputeQueueItem candidate = jobs[i];
                if (candidate.localState != HeavyComputeLocalState.Ready ||
                    _handledReadyJobs.Contains(candidate.JobId) ||
                    _promotionRetryUnixMs.TryGetValue(candidate.JobId, out long retryAt) &&
                    retryAt > unixMilliseconds)
                    continue;
                ready = candidate;
                return true;
            }
            return false;
        }

        internal async Task<bool> ReconcileOneReadyArtifactAsync(
            CancellationToken cancellationToken = default)
        {
            if (!TryFindReadyForPromotion(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out var item))
                return false;
            await PromoteAndNotifyAsync(item, cancellationToken);
            return true;
        }

        private async Task PromoteAndNotifyAsync(HeavyComputeQueueItem item,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!TryResolvePromotionContext(item, out WorldStore store,
                        out WorldManifest manifest, out ChunkRecord chunk, out string error))
                {
                    RetryPromotion(item.JobId, error);
                    return;
                }
                string artifactPath = _queue.ResolveOwnedRelativePath(
                    item.artifactRelativePath);
                long now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Math.Max(manifest.updatedUnixMilliseconds, chunk.updatedUnixMilliseconds));
                DiffSoupArtifactPublishResult result =
                    await DiffSoupArtifactPublisher.PublishAsync(store, manifest, chunk, item,
                        artifactPath, now, null, cancellationToken);
                if (result.Success)
                {
                    _handledReadyJobs.Add(item.JobId);
                    _promotionRetryUnixMs.Remove(item.JobId);
                    ArtifactReady?.Invoke(item);
                    ArtifactPromoted?.Invoke(item, result);
                    return;
                }
                if (result.Failure == DiffSoupArtifactPublishFailure.Canceled &&
                    cancellationToken.IsCancellationRequested)
                    return;
                if (result.Failure == DiffSoupArtifactPublishFailure.TransientStorage)
                {
                    RetryPromotion(item.JobId, result.Error);
                    return;
                }
                FinalizePromotionFailure(item.JobId, result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                RetryPromotion(item.JobId, exception.Message);
            }
        }

        private bool TryResolvePromotionContext(HeavyComputeQueueItem item,
            out WorldStore store, out WorldManifest manifest, out ChunkRecord chunk,
            out string error)
        {
            store = null;
            manifest = null;
            chunk = null;
            error = null;
            HeavyComputeJobKey key = item?.submission?.key;
            if (key == null)
            {
                error = "Ready job has no key.";
                return false;
            }
            if (_submaps?.Store != null && _submaps.Manifest != null &&
                string.Equals(_submaps.Manifest.worldId, key.worldId,
                    StringComparison.Ordinal))
            {
                store = _submaps.Store;
                manifest = _submaps.Manifest;
            }
            else
            {
                store = _worldStore;
                if (store == null || !store.TryLoadManifest(key.worldId, out manifest,
                        out _, out error))
                    return false;
            }
            chunk = manifest.chunks.Find(candidate => candidate != null &&
                string.Equals(candidate.chunkId, key.chunkId, StringComparison.Ordinal));
            if (chunk == null)
            {
                error = "Ready job chunk no longer exists.";
                return false;
            }
            return true;
        }

        private void RetryPromotion(string jobId, string error)
        {
            _promotionRetryUnixMs[jobId] = checked(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5_000);
            Logger.Warning("DiffSoup artifact promotion will retry: " + error);
        }

        private void FinalizePromotionFailure(string jobId,
            DiffSoupArtifactPublishResult result)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool stale = result.Failure == DiffSoupArtifactPublishFailure.StaleRevision;
            string message = result.Error ?? "DiffSoup artifact promotion failed.";
            if (message.Length > 1024) message = message.Substring(0, 1024);
            if (_queue.TryApply(jobId, current =>
                {
                    current.localState = stale
                        ? HeavyComputeLocalState.Superseded
                        : HeavyComputeLocalState.Failed;
                    current.artifactRelativePath = string.Empty;
                    if (stale) current.artifactBundle = null;
                    current.nextAttemptUnixMs = long.MaxValue;
                    current.updatedUnixMs = Math.Max(current.updatedUnixMs, now);
                    current.errorCode = stale
                        ? "superseded_revision"
                        : result.Failure == DiffSoupArtifactPublishFailure.ImmutableConflict
                            ? "artifact_conflict"
                            : "artifact_rejected";
                    current.message = message;
                    return true;
                }, out _, out string queueError))
            {
                _handledReadyJobs.Add(jobId);
                _promotionRetryUnixMs.Remove(jobId);
                QueueChanged?.Invoke();
                Logger.Error("DiffSoup artifact was not promoted: " + message);
            }
            else
            {
                RetryPromotion(jobId, queueError);
            }
        }

        private HeavyComputeQueueItem FindJob(string jobId) => Find(_queue.Snapshot(), jobId);

        private static HeavyComputeQueueItem Find(IReadOnlyList<HeavyComputeQueueItem> jobs,
            string jobId)
        {
            for (int i = 0; i < jobs.Count; i++)
                if (string.Equals(jobs[i].JobId, jobId, StringComparison.Ordinal))
                    return jobs[i];
            return null;
        }
    }
}
