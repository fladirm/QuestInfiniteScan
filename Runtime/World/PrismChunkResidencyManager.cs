using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Bridges the existing proven world/session lifecycle to native Cone-PRISM
    /// chunks. Rollover snapshots are compacted on GPU before the live arenas are
    /// reused; disk publication is revision-atomic and revisits restore the exact
    /// information posterior rather than starting a new scan.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(80)]
    public sealed class PrismChunkResidencyManager : MonoBehaviour
    {
        [SerializeField, Range(2, 12)] private int recentSnapshotLimit = 3;

        private RoomScanner _scanner;
        private SubmapManager _submaps;
        private PrismChunkSnapshotStager _stager;
        private PrismWorldMeshletRenderer _worldRenderer;
        private CancellationTokenSource _lifetime;
        private readonly Dictionary<string, Task<PrismCanonicalChunkSnapshot>>
            _staging = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PrismCanonicalChunkSnapshot> _recent =
            new(StringComparer.Ordinal);
        private readonly LinkedList<string> _recentLru = new();
        private Task _activationTail = Task.CompletedTask;
        private bool _subscribed;

        public bool IsTransitioning => !_activationTail.IsCompleted;
        public int RecentCanonicalCount => _recent.Count;

        private void Start() => Bind();

        private void Bind()
        {
            if (_subscribed) return;
            _scanner = GetComponent<RoomScanner>();
            _submaps = GetComponent<SubmapManager>();
            if (_scanner == null || _submaps == null) return;
            _stager = GetComponent<PrismChunkSnapshotStager>();
            if (_stager == null)
                _stager = gameObject.AddComponent<PrismChunkSnapshotStager>();
            _worldRenderer = GetComponent<PrismWorldMeshletRenderer>();
            if (_worldRenderer == null)
                _worldRenderer = gameObject.AddComponent<PrismWorldMeshletRenderer>();
            _lifetime = new CancellationTokenSource();
            _submaps.RolloverRequested += OnRolloverRequested;
            _submaps.ActiveChunkChanged += OnActiveChunkChanged;
            _submaps.ChunkRevisionPublished += OnLegacyRevisionPublished;
            _submaps.PoseGraphRefined += OnPoseGraphRefined;
            _scanner.ScanStarted += OnScanStarted;
            _scanner.ScanStopped += OnScanStopped;
            _subscribed = true;
            if (_submaps.ActiveChunk != null)
                OnActiveChunkChanged(_submaps.ActiveChunk);
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                _submaps.RolloverRequested -= OnRolloverRequested;
                _submaps.ActiveChunkChanged -= OnActiveChunkChanged;
                _submaps.ChunkRevisionPublished -= OnLegacyRevisionPublished;
                _submaps.PoseGraphRefined -= OnPoseGraphRefined;
                _scanner.ScanStarted -= OnScanStarted;
                _scanner.ScanStopped -= OnScanStopped;
            }
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            _subscribed = false;
        }

        private void OnRolloverRequested(SubmapRolloverRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.SourceChunkId)) return;
            BeginStage(request.SourceChunkId);
        }

        private void OnPoseGraphRefined(PoseGraphRefinementResult _)
        {
            WorldManifest manifest = _submaps?.Manifest;
            if (manifest?.chunks == null) return;
            foreach (ChunkRecord chunk in manifest.chunks)
            {
                if (chunk == null) continue;
                _worldRenderer?.SetResidentTransform(chunk.chunkId,
                    chunk.worldFromChunk.ToMatrix());
            }
            _scanner.ConfigurePrismChunk(_submaps.ActiveChunk);
        }

        private void OnScanStopped()
        {
            ChunkRecord active = _submaps?.ActiveChunk;
            if (active != null) BeginStage(active.chunkId);
        }

        private void OnScanStarted()
        {
            ChunkRecord active = _submaps?.ActiveChunk;
            if (active != null) OnActiveChunkChanged(active);
        }

        private void BeginStage(string chunkId)
        {
            if (_lifetime == null || _staging.ContainsKey(chunkId)) return;
            ContactFilmPool films = _scanner.PrismFilmSpawner?.FilmPool;
            ContactBoundaryPool boundaries =
                _scanner.PrismBoundaryGraph?.BoundaryPool;
            ContactDisplacementPool displacement =
                _scanner.PrismDisplacementTopology?.DisplacementPool;
            ContactMeshletBuffers meshlets =
                _scanner.PrismPredictionRenderer?.Meshlets;
            if (films == null || boundaries == null || displacement == null ||
                meshlets == null) return;
            uint numericId = PrismChunkIdentity.ToNumericId(chunkId);
            ulong epoch = _scanner.PrismRigCapture?.CalibrationEpoch ?? 0u;
            Task<PrismCanonicalChunkSnapshot> task = _stager.StageAsync(numericId,
                films, boundaries, displacement, meshlets, epoch,
                cancellationToken: _lifetime.Token);
            _staging.Add(chunkId, task);
            _ = RetainWhenReadyAsync(chunkId, task, _lifetime.Token);
        }

        private async Task RetainWhenReadyAsync(string chunkId,
            Task<PrismCanonicalChunkSnapshot> task, CancellationToken token)
        {
            try
            {
                PrismCanonicalChunkSnapshot snapshot = await task;
                token.ThrowIfCancellationRequested();
                RetainRecent(chunkId, snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Logger.Error($"PRISM chunk {chunkId} staging failed: " +
                    exception.Message);
            }
        }

        private void OnActiveChunkChanged(ChunkRecord chunk)
        {
            if (chunk == null || _lifetime == null) return;
            _scanner.ConfigurePrismChunk(chunk);
            _worldRenderer?.SetActive(chunk.chunkId,
                _scanner.PrismPredictionRenderer?.Meshlets);
            if (!_scanner.IsScanning) return;
            Task prior = _activationTail;
            _activationTail = ActivateAfterAsync(prior, chunk, _lifetime.Token);
        }

        private async Task ActivateAfterAsync(Task prior, ChunkRecord chunk,
            CancellationToken token)
        {
            try
            {
                if (prior != null) await prior;
                token.ThrowIfCancellationRequested();
                PrismCanonicalChunkSnapshot snapshot = await ResolveSnapshotAsync(
                    chunk, token);
                token.ThrowIfCancellationRequested();
                if (_submaps?.ActiveChunk == null ||
                    !string.Equals(_submaps.ActiveChunk.chunkId, chunk.chunkId,
                        StringComparison.Ordinal))
                    return;

                _scanner.PausePrismForResidency();
                await AwaitGpuIdleAsync(token);
                ContactFilmPool films = _scanner.PrismFilmSpawner?.FilmPool;
                ContactBoundaryPool boundaries =
                    _scanner.PrismBoundaryGraph?.BoundaryPool;
                ContactDisplacementPool displacement =
                    _scanner.PrismDisplacementTopology?.DisplacementPool;
                ContactMeshletBuffers meshlets =
                    _scanner.PrismPredictionRenderer?.Meshlets;
                if (films == null || boundaries == null || displacement == null ||
                    meshlets == null)
                    throw new InvalidOperationException(
                        "Cone-PRISM pools are unavailable during chunk activation.");

                Matrix4x4 worldFromChunk = chunk.worldFromChunk.ToMatrix();
                if (snapshot == null)
                {
                    uint nextGeneration = unchecked(
                        (uint)Math.Max(1, chunk.revision + 1));
                    PrismGpuSnapshotRestore.ClearInPlace(films, boundaries,
                        displacement, meshlets, nextGeneration, worldFromChunk);
                }
                else if (!PrismGpuSnapshotRestore.TryRestoreInPlace(snapshot,
                             films, boundaries, displacement, meshlets,
                             worldFromChunk, out string error))
                {
                    throw new InvalidDataException(error);
                }
                _scanner.PrismBoundaryGraph?.RebuildCanonicalIndex();
                _scanner.ConfigurePrismChunk(chunk);
                _worldRenderer?.SetActive(chunk.chunkId, meshlets);
                Logger.Info(snapshot == null
                    ? $"Cone-PRISM activated empty {chunk.chunkId}"
                    : $"Cone-PRISM resumed {chunk.chunkId} revision {chunk.revision} " +
                      $"with {snapshot.FilmCount} films");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM chunk activation failed: {exception}");
            }
            finally
            {
                if (_scanner != null) _scanner.ResumePrismAfterResidency();
            }
        }

        private async Task<PrismCanonicalChunkSnapshot> ResolveSnapshotAsync(
            ChunkRecord chunk, CancellationToken token)
        {
            if (_staging.TryGetValue(chunk.chunkId,
                    out Task<PrismCanonicalChunkSnapshot> staged))
                return await staged;
            if (_recent.TryGetValue(chunk.chunkId,
                    out PrismCanonicalChunkSnapshot recent))
            {
                TouchRecent(chunk.chunkId);
                return recent;
            }
            ChunkArtifactRecord artifact = chunk.artifacts?.Find(candidate =>
                candidate != null &&
                candidate.kind == ChunkArtifactKind.PrismCanonical);
            if (artifact == null) return null;
            WorldStore store = _submaps.Store;
            string worldId = _submaps.Manifest.worldId;
            return await Task.Run(() => LoadSnapshot(store, worldId, artifact), token);
        }

        private async void OnLegacyRevisionPublished(WorldStore store, string worldId,
            string chunkId, int revision)
        {
            try
            {
                if (!_staging.TryGetValue(chunkId,
                        out Task<PrismCanonicalChunkSnapshot> staged)) return;
                PrismCanonicalChunkSnapshot snapshot = await staged;
                if (_lifetime == null || _lifetime.IsCancellationRequested) return;
                ChunkRecord chunk = _submaps.Manifest?.chunks?.Find(candidate =>
                    candidate != null && string.Equals(candidate.chunkId, chunkId,
                        StringComparison.Ordinal));
                if (chunk == null || chunk.revision < revision) return;
                PrismChunkPublishResult result = await PrismChunkPublisher.PublishAsync(
                    store, _submaps.Manifest, chunk, snapshot,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (!result.Success)
                    throw new IOException(result.Error);
                _staging.Remove(chunkId);
                RetainRecent(chunkId, snapshot);
                Logger.Info($"Published Cone-PRISM {chunkId} revision " +
                    result.Revision);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM publication failed for {chunkId}: " +
                    exception.Message);
            }
        }

        private void RetainRecent(string chunkId,
            PrismCanonicalChunkSnapshot snapshot)
        {
            _recent[chunkId] = snapshot;
            ChunkRecord chunk = _submaps?.Manifest?.chunks?.Find(candidate =>
                candidate != null && string.Equals(candidate.chunkId, chunkId,
                    StringComparison.Ordinal));
            if (chunk != null && snapshot.MeshletDescriptorCount > 0)
            {
                if (PrismGpuSnapshotRestore.TryCreateMeshletCache(snapshot,
                        chunk.worldFromChunk.ToMatrix(),
                        out ContactMeshletBuffers cache, out string cacheError))
                    _worldRenderer?.RegisterResident(chunkId, cache);
                else
                    Logger.Warning($"PRISM preview cache {chunkId} skipped: " +
                        cacheError);
            }
            TouchRecent(chunkId);
            int limit = Mathf.Max(2, recentSnapshotLimit);
            while (_recent.Count > limit && _recentLru.First != null)
            {
                string candidate = _recentLru.First.Value;
                _recentLru.RemoveFirst();
                if (_submaps?.ActiveChunk != null && string.Equals(candidate,
                        _submaps.ActiveChunk.chunkId, StringComparison.Ordinal))
                {
                    _recentLru.AddLast(candidate);
                    continue;
                }
                _recent.Remove(candidate);
                _worldRenderer?.RemoveResident(candidate);
            }
        }

        private void TouchRecent(string chunkId)
        {
            LinkedListNode<string> node = _recentLru.Find(chunkId);
            if (node != null) _recentLru.Remove(node);
            _recentLru.AddLast(chunkId);
        }

        private static PrismCanonicalChunkSnapshot LoadSnapshot(WorldStore store,
            string worldId, ChunkArtifactRecord artifact)
        {
            if (!store.TryResolveVerifiedArtifact(worldId, artifact, out string path,
                    out string error))
                throw new InvalidDataException(error);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            if (!PrismCanonicalChunkCodec.TryRead(stream, out var snapshot, out error))
                throw new InvalidDataException(error);
            return snapshot;
        }

        private static async Task AwaitGpuIdleAsync(CancellationToken token)
        {
            GraphicsFence fence;
            try
            {
                fence = Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
            }
            catch (Exception)
            {
                await Task.Yield();
                return;
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try { if (fence.passed) return; }
                catch (Exception) { return; }
                await Task.Yield();
            }
        }
    }
}
