using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.HeavyCompute;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace Genesis.RoomScan.World
{
    internal sealed class ChunkRefinementContext
    {
        internal WorldStore Store;
        internal WorldManifest Manifest;
        internal ChunkRecord Chunk;
        internal string WorldId;
        internal string ChunkId;
        internal string KeyframeDirectory;
        internal RigidPoseData WorldFromChunk;
    }

    /// <summary>
    /// Optional large-world module. It observes the headset against the active local volume
    /// and finalizes/publishes source payloads outside the render-frame file-I/O path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SubmapManager : MonoBehaviour, IRoomScanModule
    {
        [Header("Large World")]
        [SerializeField] private bool largeWorldMode;
        [SerializeField] private SubmapRolloverSettings rollover = new();
        [SerializeField, Tooltip("Finalize and advance automatically at a chunk boundary")]
        private bool autoFinalizeRollover = true;
        [SerializeField, Tooltip("Optional conventional vertex-color material for finalized chunks")]
        private Material persistedChunkMaterial;
        [SerializeField, Min(0.25f)] private float finalizationRetrySeconds = 2f;
        [SerializeField, Tooltip("Run bounded background point-to-plane ICP between consecutive finalized overlaps")]
        private bool enableOverlapIcp = true;
        [SerializeField, Range(64, 16384)] private int overlapIcpMaximumSamples = 4096;
        [SerializeField, Min(1f), Tooltip("Interval for bounded world/residency telemetry used by the ADB performance harness")]
        private float worldProfilingIntervalSeconds = 5f;

        private RoomScanner _scanner;
        private VolumeIntegrator _volume;
        private WorldStore _store;
        private SubmapRolloverController _controller;
        private SubmapRolloverRequest _lastRaisedRequest;
        private PersistedChunkMeshCache _meshCache;
        private DiffSoupRendererCache _diffSoupCache;
        private KeyframeCollector _keyframes;
        private Task _finalizationTask;
        private Task _restoreTask;
        private Task _keyframeRestoreTask;
        private SubmapRolloverRequest _preparedRequest;
        private ChunkGpuSnapshot _preparedSnapshot;
        private string _preparedKeyframeDirectory;
        private ChunkVolumeSnapshot _preparedTargetVolume;
        private string _recentVolumeChunkId;
        private ChunkVolumeSnapshot _recentVolumeSnapshot;
        private int _backgroundPublicationCount;
        private float _retryAfterUnscaledTime;
        private PoseGraphRefinementCoordinator _poseGraphRefinement;
        private CancellationTokenSource _poseGraphCancellation;
        private Task _poseGraphTask;
        private OverlapObservation _previousOverlap;
        private int _overlapGeneration;
        private float _nextWorldProfileTime;
        private readonly Dictionary<string, Task> _chunkPublicationTails =
            new(StringComparer.Ordinal);

        public string ModuleName => "Infinite Submaps";
        public bool LargeWorldMode
        {
            get => largeWorldMode;
            set
            {
                largeWorldMode = value;
                if (value)
                    GetComponent<TriplanarCache>()?.SetTriplanarEnabled(false);
            }
        }
        public bool HasWorld => _controller != null;
        internal WorldStore Store => _store;
        public WorldManifest Manifest => _controller?.Manifest;
        public ChunkRecord ActiveChunk => _controller?.ActiveChunk;
        public SubmapRolloverRequest PendingRequest => _controller?.PendingRequest;
        public int ResidentVolumeCount => _controller?.ResidentVolumeCount ?? 0;
        public int MaximumResidentVolumeCount => 1;
        public int ResidentPersistedMeshCount => _meshCache != null ? _meshCache.Count : 0;
        public int ResidentDiffSoupCount => _diffSoupCache != null ? _diffSoupCache.Count : 0;
        public int MaximumResidentChunkMeshCount =>
            Mathf.Max(1, rollover.maximumResidentChunkMeshes);
        public float BoundaryMarginMeters => rollover.boundaryMarginMeters;
        public float OverlapMeters => rollover.overlapMeters;
        public float RearmHysteresisMeters => rollover.rearmHysteresisMeters;
        public bool UsesLargeWorldDefaults => rollover.UsesLargeWorldDefaults;
        public bool IsFinalizing => _finalizationTask != null && !_finalizationTask.IsCompleted;
        /// <summary>
        /// True only while the single reusable TSDF must not receive another integration.
        /// Large disk writes intentionally do not hold this gate: they continue after the
        /// volume has switched to the next chunk.
        /// </summary>
        public bool IsVolumeTransitioning =>
            _controller?.PendingRequest != null &&
            (IsFinalizing || _preparedRequest != null);
        public int BackgroundPublicationCount => _backgroundPublicationCount;
        public bool IsPoseGraphRefining => _poseGraphTask != null &&
                                           !_poseGraphTask.IsCompleted;
        public string PoseGraphStatus { get; private set; } = "Idle";
        public bool IsRestoring =>
            _restoreTask != null && !_restoreTask.IsCompleted ||
            _keyframeRestoreTask != null && !_keyframeRestoreTask.IsCompleted;
        public string FinalizationStatus { get; private set; } = "Idle";

        public event Action<SubmapRolloverRequest> RolloverRequested;
        public event Action<ChunkRecord> ActiveChunkChanged;
        public event Action<string> FinalizationStatusChanged;
        public event Action<WorldStore, string, string, int> ChunkRevisionPublished;
        public event Action<PoseGraphRefinementResult> PoseGraphRefined;

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

        internal async Task WaitForRefinementReadyAsync()
        {
            Task finalization = _finalizationTask;
            if (finalization != null && !finalization.IsCompleted)
                await finalization;
            ChunkRecord chunk = _controller?.ActiveChunk;
            WorldManifest manifest = _controller?.Manifest;
            if (chunk == null || manifest == null)
                return;
            Task publication = GetChunkPublicationTail(manifest.worldId, chunk.chunkId);
            if (publication != null && !publication.IsCompleted)
                await publication;
        }

        internal bool TryCaptureRefinementContext(out ChunkRefinementContext context,
            out string error)
        {
            context = null;
            error = null;
            if (!largeWorldMode)
            {
                error = "Large-world mode is not enabled.";
                return false;
            }
            if (_scanner != null && _scanner.IsScanning)
            {
                error = "Stop scanning before refining an active chunk.";
                return false;
            }
            ChunkRecord chunk = _controller?.ActiveChunk;
            WorldManifest manifest = _controller?.Manifest;
            if (_store == null || manifest == null || chunk == null)
            {
                error = "No active infinite-world chunk is available.";
                return false;
            }
            string keyframes = GetChunkKeyframeDirectory(chunk);
            if (!File.Exists(Path.Combine(keyframes, "frames.jsonl")))
            {
                error = $"Chunk {chunk.chunkId} has no keyframe manifest.";
                return false;
            }
            context = new ChunkRefinementContext
            {
                Store = _store,
                Manifest = manifest,
                Chunk = chunk,
                WorldId = manifest.worldId,
                ChunkId = chunk.chunkId,
                KeyframeDirectory = keyframes,
                WorldFromChunk = chunk.worldFromChunk
            };
            return true;
        }

        internal async Task<ChunkRefinedPublishResult> PublishRefinedArtifactsAsync(
            ChunkRefinementContext context, RefinedTextureResult refined)
        {
            if (context?.Store == null || context.Manifest == null || context.Chunk == null ||
                !string.Equals(context.WorldId, context.Manifest.worldId,
                    StringComparison.Ordinal) ||
                !string.Equals(context.ChunkId, context.Chunk.chunkId,
                    StringComparison.Ordinal) ||
                !context.Manifest.chunks.Contains(context.Chunk))
            {
                return new ChunkRefinedPublishResult
                {
                    Error = "Refinement context no longer identifies a valid chunk."
                };
            }

            Task pending = GetChunkPublicationTail(context.WorldId, context.ChunkId);
            if (pending != null && !pending.IsCompleted)
                await pending;

            const int maximumAttempts = 3;
            ChunkRefinedPublishResult result = null;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                long now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    context.Manifest.updatedUnixMilliseconds);
                result = await ChunkRefinedArtifactPublisher.PublishAsync(context.Store,
                    context.Manifest, context.Chunk, refined, now);
                if (result.Success)
                {
                    ChunkRevisionPublished?.Invoke(context.Store, context.WorldId,
                        context.ChunkId, result.Revision);
                    return result;
                }
                if (attempt < maximumAttempts &&
                    result.Error != null && result.Error.Contains("revision",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(attempt * 100);
                    continue;
                }
                break;
            }
            return result ?? new ChunkRefinedPublishResult
            {
                Error = "Refined artifact publication did not run."
            };
        }

        private void Awake()
        {
            if (largeWorldMode)
                GetComponent<TriplanarCache>()?.SetTriplanarEnabled(false);
        }

        public void OnModuleInitialize(RoomScanner scanner)
        {
            _scanner = scanner;
            _volume = scanner != null ? scanner.VolumeIntegrator : GetComponent<VolumeIntegrator>();
            _keyframes = scanner != null ? scanner.KeyframeCollector :
                GetComponent<KeyframeCollector>();
            _meshCache = GetComponent<PersistedChunkMeshCache>();
            if (_meshCache == null)
                _meshCache = gameObject.AddComponent<PersistedChunkMeshCache>();
            _meshCache.Initialize(persistedChunkMaterial,
                Mathf.Max(0, rollover.maximumResidentChunkMeshes - 1));
            _diffSoupCache = GetComponent<DiffSoupRendererCache>();
            if (_diffSoupCache == null)
                _diffSoupCache = gameObject.AddComponent<DiffSoupRendererCache>();
            _diffSoupCache.Initialize(scanner, this, _meshCache,
                Mathf.Max(0, rollover.maximumResidentChunkMeshes - 1));
            _poseGraphRefinement?.Dispose();
            _poseGraphRefinement = new PoseGraphRefinementCoordinator(
                new PointToPlaneIcpEstimator(new PointToPlaneIcpSettings
                {
                    MaximumSamples = Mathf.Clamp(overlapIcpMaximumSamples, 64, 16384)
                }));
            ResetOverlapPipeline();
            if (_scanner != null)
            {
                _scanner.ScanAnchorCreated += OnScanAnchorCreated;
                _scanner.RenderModeChanged += OnRenderModeChanged;
                _meshCache.SetRenderMode(_scanner.CurrentRenderMode);
            }
        }

        private void OnDestroy()
        {
            _overlapGeneration++;
            _poseGraphCancellation?.Cancel();
            _poseGraphCancellation?.Dispose();
            _poseGraphCancellation = null;
            _poseGraphRefinement?.Dispose();
            _poseGraphRefinement = null;
            if (_scanner != null)
            {
                _scanner.ScanAnchorCreated -= OnScanAnchorCreated;
                _scanner.RenderModeChanged -= OnRenderModeChanged;
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
                _controller.PendingRequest != null || IsFinalizing || IsRestoring ||
                _volume == null || _volume.IntegrationCount <= 0)
                return;
            _finalizationTask = FinalizeActiveChunkAsync();
        }

        public bool TryStartNewWorld(string worldId, string displayName, Pose cameraWorldPose,
            out string error)
        {
            error = null;
            _volume ??= _scanner != null ? _scanner.VolumeIntegrator :
                GetComponent<VolumeIntegrator>();
            if (_volume == null)
            {
                error = "VolumeIntegrator is missing.";
                return false;
            }

            int3 count = _volume.VoxelCount;
            var extents = new Vector3(count.x, count.y, count.z) *
                          (_volume.VoxelSize * 0.5f);
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
            if (!_volume.TrySetWorldFromVolume(initialPose, out error))
                return false;

            _store = store;
            _controller = controller;
            _lastRaisedRequest = null;
            _preparedRequest = null;
            _preparedSnapshot = null;
            _preparedKeyframeDirectory = null;
            _preparedTargetVolume = null;
            _recentVolumeChunkId = null;
            _recentVolumeSnapshot = null;
            ResetOverlapPipeline();
            _keyframeRestoreTask = null;
            _diffSoupCache?.Clear();
            _meshCache?.Clear();
            _volume.ReallocateVolumes();
            _volume.Clear();
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
            _volume ??= _scanner != null ? _scanner.VolumeIntegrator :
                GetComponent<VolumeIntegrator>();
            if (_volume == null ||
                !_volume.TrySetWorldFromVolume(controller.ActiveChunk.worldFromChunk, out error))
                return false;
            _diffSoupCache?.Clear();
            _meshCache?.Clear();
            _store = store;
            _controller = controller;
            _lastRaisedRequest = null;
            _preparedRequest = null;
            _preparedSnapshot = null;
            _preparedKeyframeDirectory = null;
            _preparedTargetVolume = null;
            _recentVolumeChunkId = null;
            _recentVolumeSnapshot = null;
            ResetOverlapPipeline();
            _scanner?.ConfigurePrismChunk(_controller.ActiveChunk);
            ActiveChunkChanged?.Invoke(_controller.ActiveChunk);
            _volume.ReallocateVolumes();
            _volume.Clear();
            _restoreTask = RestoreActiveVolumeAsync();
            BeginRestoreChunkKeyframes(_controller.ActiveChunk);
            if (_meshCache != null)
            {
                Vector3 cameraPosition = Camera.main != null
                    ? Camera.main.transform.position
                    : controller.ActiveChunk.worldFromChunk.position;
                _ = _meshCache.RestoreNearestAsync(store, manifest, cameraPosition);
            }
            EmitWorldProfile("attach");
            return true;
        }

        /// <summary>
        /// Call only after the source chunk payload is durable. The method atomically publishes
        /// the new graph vertex, clears the reusable TSDF, and places it in the target frame.
        /// </summary>
        public bool TryCompletePendingRollover(out string error)
        {
            return TryCompletePendingRollover(ChunkLifecycleState.Persisted, null,
                out _, out _, out error);
        }

        private bool TryCompletePendingRollover(ChunkLifecycleState sourceState,
            ChunkVolumeSnapshot targetVolume, out ChunkRecord source,
            out ChunkRecord target, out string error)
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
            ChunkRecord pendingTarget = pending.IsRevisit
                ? FindChunk(pending.TargetChunkId)
                : null;
            if (pending.IsRevisit &&
                !IsCompatibleVolumeSnapshot(targetVolume, pendingTarget, _volume,
                    out error))
                return false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!_controller.TryCommitPending(_store, now, sourceState,
                    out target, out error))
                return false;
            if (!_volume.TrySetWorldFromVolume(target.worldFromChunk, out error))
                return false;
            if (pending.IsRevisit)
            {
                if (!_volume.LoadVolumes(targetVolume.TsdfBytes, targetVolume.ColorBytes,
                        targetVolume.IntegrationCount))
                {
                    error = "VolumeIntegrator rejected the revisit payload.";
                    return false;
                }
                _meshCache?.Remove(target.chunkId);
                BeginRestoreChunkKeyframes(target);
            }
            else
            {
                _volume.Clear();
                ConfigureChunkKeyframes(target, false);
            }
            _scanner?.MeshExtractor?.ResetTemporalState();
            _scanner?.MeshExtractor?.Extract();
            _lastRaisedRequest = null;
            if (_keyframes != null)
                _keyframes.CaptureEnabled = !pending.IsRevisit ||
                    _keyframeRestoreTask == null || _keyframeRestoreTask.IsCompleted;
            _scanner?.ConfigurePrismChunk(target);
            ActiveChunkChanged?.Invoke(target);
            _meshCache?.RefreshTransforms(_controller.Manifest);
            _diffSoupCache?.RefreshTransforms(_controller.Manifest);
            Logger.Info($"Submap rollover complete: active={target.chunkId}, " +
                        $"revisit={pending.IsRevisit}, residentVolumes={ResidentVolumeCount}");
            EmitWorldProfile("rollover");
            return true;
        }

        public void CancelPendingRollover()
        {
            _controller?.CancelPending();
            _lastRaisedRequest = null;
            _preparedRequest = null;
            _preparedSnapshot = null;
            _preparedKeyframeDirectory = null;
            _preparedTargetVolume = null;
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
            if (_preparedRequest != null && Time.unscaledTime >= _retryAfterUnscaledTime)
            {
                TryCompletePreparedRollover();
                return;
            }
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
                    _finalizationTask = FinalizeAndAdvanceAsync(request);
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
                manifest.edges?.Count ?? 0, ResidentVolumeCount,
                MaximumResidentVolumeCount, ResidentPersistedMeshCount,
                ResidentDiffSoupCount, BackgroundPublicationCount,
                Profiler.GetTotalAllocatedMemoryLong(),
                Profiler.GetTotalReservedMemoryLong()));
        }

        /// <summary>
        /// Restores the active chunk's last durable mapper snapshot after attaching a world.
        /// File read, SHA validation, and decoding run on a worker; texture upload resumes on
        /// Unity's captured context. A chunk with no snapshot intentionally starts empty.
        /// Returns null on success or a diagnostic string on failure.
        /// </summary>
        public async Task<string> RestoreActiveVolumeAsync()
        {
            if (_store == null || _controller?.ActiveChunk == null || _volume == null)
                return "No attached world is available for volume restore.";
            ChunkRecord chunk = _controller.ActiveChunk;
            ChunkArtifactRecord artifact = chunk.artifacts?.Find(candidate =>
                candidate.kind == ChunkArtifactKind.Volume);
            if (artifact == null)
            {
                SetFinalizationStatus("Idle");
                return null;
            }

            SetFinalizationStatus("Restoring active chunk");
            string worldId = _controller.Manifest.worldId;
            VolumeArtifactLoadResult loaded = await Task.Run(() =>
                LoadVolumeArtifact(_store, worldId, artifact));
            if (loaded.Snapshot == null)
            {
                SetFinalizationStatus("Restore failed: " + loaded.Error);
                Logger.Error("Active chunk restore failed: " + loaded.Error);
                return loaded.Error;
            }
            if (_controller == null || !ReferenceEquals(chunk, _controller.ActiveChunk))
                return "Active chunk changed while its volume was loading.";
            if (loaded.Snapshot.VoxelCount.x != _volume.VoxelCount.x ||
                loaded.Snapshot.VoxelCount.y != _volume.VoxelCount.y ||
                loaded.Snapshot.VoxelCount.z != _volume.VoxelCount.z ||
                Mathf.Abs(loaded.Snapshot.VoxelSize - _volume.VoxelSize) > 0.0001f)
            {
                const string mismatch = "Active chunk snapshot is incompatible with the mapper.";
                SetFinalizationStatus("Restore failed: " + mismatch);
                return mismatch;
            }

            _volume.ReallocateVolumes();
            if (!_volume.TrySetWorldFromVolume(chunk.worldFromChunk, out string poseError))
                return poseError;
            if (!_volume.LoadVolumes(loaded.Snapshot.TsdfBytes,
                    loaded.Snapshot.ColorBytes, loaded.Snapshot.IntegrationCount))
                return "VolumeIntegrator rejected the restored payload.";
            _scanner?.MeshExtractor?.EnsureInitialized();
            _scanner?.MeshExtractor?.ResetTemporalState();
            _scanner?.MeshExtractor?.Extract();
            SetFinalizationStatus("Idle");
            Logger.Info($"Restored active chunk {chunk.chunkId} revision {chunk.revision}");
            return null;
        }

        private async Task FinalizeAndAdvanceAsync(SubmapRolloverRequest request)
        {
            SetFinalizationStatus("Capturing GPU snapshot");
            try
            {
                if (_controller == null || _store == null || _volume == null ||
                    request == null || !ReferenceEquals(request, _controller.PendingRequest))
                    throw new InvalidOperationException("Rollover request is no longer active.");
                ChunkRecord source = FindChunk(request.SourceChunkId);
                if (source == null)
                    throw new InvalidOperationException("Source chunk is missing from the world.");

                ChunkVolumeSnapshot revisitVolume = null;
                Task<VolumeArtifactLoadResult> revisitLoadTask = null;
                if (request.IsRevisit)
                {
                    if (string.Equals(_recentVolumeChunkId, request.TargetChunkId,
                            StringComparison.Ordinal))
                    {
                        revisitVolume = _recentVolumeSnapshot;
                    }
                    else
                    {
                        ChunkRecord revisitChunk = FindChunk(request.TargetChunkId);
                        ChunkArtifactRecord volumeArtifact = revisitChunk?.artifacts?.Find(
                            candidate => candidate.kind == ChunkArtifactKind.Volume);
                        if (volumeArtifact == null)
                            throw new InvalidOperationException(
                                $"Revisit target {request.TargetChunkId} has no available " +
                                "volume snapshot.");
                        string worldId = _controller.Manifest.worldId;
                        revisitLoadTask = Task.Run(() => LoadVolumeArtifact(_store,
                            worldId, volumeArtifact));
                    }
                }

                if (_keyframes != null)
                    _keyframes.CaptureEnabled = false;
                if (_keyframes != null &&
                    !await _keyframes.WaitForPendingWritesAsync())
                    throw new IOException("Timed out waiting for source keyframe writes.");
                string keyframeDirectory = _keyframes?.ExportDirectory;

                ChunkGpuSnapshot snapshot = await ChunkGpuSnapshotCapture.CaptureAsync(
                    _volume, _scanner.MeshExtractor);
                if (revisitLoadTask != null)
                {
                    VolumeArtifactLoadResult loaded = await revisitLoadTask;
                    if (loaded.Snapshot == null)
                        throw new IOException("Revisit volume load failed: " + loaded.Error);
                    revisitVolume = loaded.Snapshot;
                }
                SetFinalizationStatus("Switching active chunk");
                _preparedRequest = request;
                _preparedSnapshot = snapshot;
                _preparedKeyframeDirectory = keyframeDirectory;
                _preparedTargetVolume = revisitVolume;
                if (!TryCompletePreparedRollover())
                    return;
            }
            catch (Exception exception)
            {
                SetFinalizationStatus("Failed: " + exception.Message);
                Logger.Error("Chunk finalization failed: " + exception);
                _preparedRequest = null;
                _preparedSnapshot = null;
                _preparedKeyframeDirectory = null;
                _preparedTargetVolume = null;
                _lastRaisedRequest = null;
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = true;
                _retryAfterUnscaledTime = Time.unscaledTime +
                    Mathf.Max(0.25f, finalizationRetrySeconds);
            }
            finally
            {
                _finalizationTask = null;
            }
        }

        private async Task FinalizeActiveChunkAsync()
        {
            SetFinalizationStatus("Saving active chunk");
            try
            {
                if (_controller?.ActiveChunk == null || _store == null || _volume == null)
                    throw new InvalidOperationException("No active chunk can be finalized.");
                ChunkRecord chunk = _controller.ActiveChunk;
                Task pendingPublication = GetChunkPublicationTail(
                    _controller.Manifest.worldId, chunk.chunkId);
                if (pendingPublication != null && !pendingPublication.IsCompleted)
                {
                    SetFinalizationStatus("Waiting for prior chunk revision");
                    await pendingPublication;
                }
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = false;
                if (_keyframes != null &&
                    !await _keyframes.WaitForPendingWritesAsync())
                    throw new IOException("Timed out waiting for active keyframe writes.");
                string keyframeDirectory = _keyframes?.ExportDirectory;
                ChunkGpuSnapshot snapshot = await ChunkGpuSnapshotCapture.CaptureAsync(
                    _volume, _scanner.MeshExtractor);
                ChunkSnapshotPublishResult publication = await ChunkSnapshotPublisher.PublishAsync(
                    _store, _controller.Manifest, chunk, snapshot,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), keyframeDirectory);
                if (!publication.Success)
                    throw new IOException(publication.Error);
                ChunkRevisionPublished?.Invoke(_store, _controller.Manifest.worldId,
                    chunk.chunkId, publication.Revision);
                QueueOverlapRefinement(chunk, snapshot.LiveMesh);
                SetFinalizationStatus("Idle");
                Logger.Info($"Saved active chunk {chunk.chunkId} revision {chunk.revision}");
            }
            catch (Exception exception)
            {
                SetFinalizationStatus("Failed: " + exception.Message);
                Logger.Error("Active chunk finalization failed: " + exception);
            }
            finally
            {
                if (_keyframes != null)
                    _keyframes.CaptureEnabled = true;
                _finalizationTask = null;
            }
        }

        private bool TryCompletePreparedRollover()
        {
            if (_preparedRequest == null || _controller == null ||
                _preparedSnapshot == null ||
                !ReferenceEquals(_preparedRequest, _controller.PendingRequest))
            {
                _preparedRequest = null;
                _preparedSnapshot = null;
                _preparedKeyframeDirectory = null;
                _preparedTargetVolume = null;
                return false;
            }

            SubmapRolloverRequest request = _preparedRequest;
            ChunkGpuSnapshot snapshot = _preparedSnapshot;
            string keyframeDirectory = _preparedKeyframeDirectory;
            ChunkVolumeSnapshot targetVolume = _preparedTargetVolume;
            if (!TryCompletePendingRollover(ChunkLifecycleState.Finalizing, targetVolume,
                    out ChunkRecord source, out ChunkRecord target, out string error))
            {
                SetFinalizationStatus("Rollover commit retry: " + error);
                _retryAfterUnscaledTime = Time.unscaledTime +
                    Mathf.Max(0.25f, finalizationRetrySeconds);
                return false;
            }

            if (source != null && snapshot.LiveMesh != null && _meshCache != null &&
                !_meshCache.TryPromote(source, snapshot.LiveMesh, out string cacheError))
                Logger.Warning($"Finalized chunk {source.chunkId} persisted but was not cached: " +
                               cacheError);
            _preparedRequest = null;
            _preparedSnapshot = null;
            _preparedKeyframeDirectory = null;
            _preparedTargetVolume = null;

            // Retain exactly one previous CPU volume. It makes an immediate reverse walk
            // restore without waiting for the just-started background write while keeping
            // memory O(1) with respect to world size.
            _recentVolumeChunkId = source.chunkId;
            _recentVolumeSnapshot = snapshot.Volume;

            long switchedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Logger.Info($"Submap live switch: {source.chunkId} -> {target.chunkId} in " +
                        $"{switchedAt - request.RequestedUnixMilliseconds}ms; " +
                        "source persistence continues in background");
            QueueBackgroundPublication(source, snapshot, keyframeDirectory);
            QueueOverlapRefinement(source, snapshot.LiveMesh);
            return true;
        }

        private void QueueOverlapRefinement(ChunkRecord chunk,
            ChunkLiveMeshSnapshot snapshot)
        {
            if (!enableOverlapIcp || _poseGraphRefinement == null || chunk == null ||
                snapshot == null || _controller?.Manifest == null)
                return;
            _poseGraphCancellation ??= new CancellationTokenSource();
            Task previous = _poseGraphTask;
            int generation = _overlapGeneration;
            string worldId = _controller.Manifest.worldId;
            string chunkId = chunk.chunkId;
            int chunkRevision = chunk.revision;
            CancellationToken token = _poseGraphCancellation.Token;
            _poseGraphTask = RefineOverlapAfterAsync(previous, generation, worldId,
                chunkId, chunkRevision, snapshot, token);
        }

        private async Task RefineOverlapAfterAsync(Task previous, int generation,
            string worldId, string chunkId, int chunkRevision,
            ChunkLiveMeshSnapshot snapshot, CancellationToken cancellationToken)
        {
            try
            {
                if (previous != null)
                    await previous;
                cancellationToken.ThrowIfCancellationRequested();
                SetPoseGraphStatus("Sampling finalized overlap");
                OverlapPointCloud cloud = null;
                string cloudError = null;
                int maximumSamples = Mathf.Clamp(overlapIcpMaximumSamples, 64, 16384);
                bool cloudBuilt = await Task.Run(() =>
                    OverlapPointCloudBuilder.TryCreate(snapshot,
                        maximumSamples,
                        out cloud, out cloudError), cancellationToken);
                if (!cloudBuilt)
                    throw new InvalidOperationException(cloudError);
                if (generation != _overlapGeneration || _controller?.Manifest == null ||
                    !string.Equals(_controller.Manifest.worldId, worldId,
                        StringComparison.Ordinal))
                    return;

                var current = new OverlapObservation(worldId, chunkId,
                    chunkRevision, cloud);
                OverlapObservation prior = _previousOverlap;
                _previousOverlap = current;
                if (prior == null || string.Equals(prior.ChunkId, current.ChunkId,
                        StringComparison.Ordinal))
                {
                    SetPoseGraphStatus("Waiting for next overlap");
                    return;
                }

                ChunkRecord source = FindChunk(prior.ChunkId);
                ChunkRecord target = FindChunk(current.ChunkId);
                if (source == null || target == null)
                    throw new InvalidOperationException(
                        "Overlap chunks disappeared before registration.");
                RigidPoseData initialSourceFromTarget = source.worldFromChunk.Inverse() *
                                                        target.worldFromChunk;
                long observed = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    _controller.Manifest.updatedUnixMilliseconds);
                if (!OverlapRegistrationRequest.TryCreate(prior.ChunkId,
                        prior.ChunkRevision, prior.Cloud, current.ChunkId,
                        current.ChunkRevision, current.Cloud,
                        initialSourceFromTarget, observed,
                        out OverlapRegistrationRequest request, out string requestError))
                    throw new InvalidOperationException(requestError);

                SetPoseGraphStatus($"Registering {prior.ChunkId} ↔ {current.ChunkId}");
                PoseGraphRefinementResult result = await
                    _poseGraphRefinement.RefineAsync(_controller.Manifest, _store,
                        request, observed, cancellationToken);
                if (!result.Succeeded)
                {
                    SetPoseGraphStatus("Rejected: " + result.Error);
                    Logger.Warning("Pose-graph overlap rejected: " + result.Error);
                    PoseGraphRefined?.Invoke(result);
                    return;
                }
                if (generation != _overlapGeneration)
                    return;
                RefreshRuntimeGraphFrames();
                SetPoseGraphStatus(string.Format(CultureInfo.InvariantCulture,
                    "Optimized: RMS {0:F3}→{1:F3} m",
                    result.Solution.InitialError.TranslationRmsMeters,
                    result.Solution.FinalError.TranslationRmsMeters));
                PoseGraphRefined?.Invoke(result);
                Logger.Info($"Pose graph optimized with {result.Edge.edgeId}: " +
                            $"inliers={result.Estimate.CorrespondenceCount}, " +
                            $"rms={result.Estimate.RmsMeters:F4}m");
            }
            catch (OperationCanceledException)
            {
                if (generation == _overlapGeneration)
                    SetPoseGraphStatus("Cancelled");
            }
            catch (Exception exception)
            {
                if (generation == _overlapGeneration)
                {
                    SetPoseGraphStatus("Failed: " + exception.Message);
                    Logger.Warning("Pose-graph overlap failed: " + exception.Message);
                }
            }
        }

        private void RefreshRuntimeGraphFrames()
        {
            ChunkRecord active = _controller?.ActiveChunk;
            if (active != null)
            {
                if (_volume != null && !_volume.TrySetWorldFromVolume(
                        active.worldFromChunk, out string volumeError))
                    Logger.Error("Optimized active volume pose was rejected: " + volumeError);
                _keyframes?.TryUpdateChunkWorldPose(active.chunkId,
                    active.worldFromChunk);
            }
            _meshCache?.RefreshTransforms(_controller?.Manifest);
            _diffSoupCache?.RefreshTransforms(_controller?.Manifest);
        }

        private void ResetOverlapPipeline()
        {
            _overlapGeneration++;
            _poseGraphCancellation?.Cancel();
            _poseGraphCancellation?.Dispose();
            _poseGraphCancellation = new CancellationTokenSource();
            _poseGraphTask = null;
            _previousOverlap = null;
            SetPoseGraphStatus(enableOverlapIcp ? "Waiting for first overlap" : "Disabled");
        }

        private void SetPoseGraphStatus(string status)
        {
            PoseGraphStatus = status ?? string.Empty;
        }

        private void QueueBackgroundPublication(ChunkRecord source, ChunkGpuSnapshot snapshot,
            string keyframeDirectory)
        {
            WorldStore store = _store;
            WorldManifest manifest = _controller?.Manifest;
            if (store == null || manifest == null || source == null)
            {
                Logger.Error("Cannot queue chunk publication without its world context.");
                return;
            }
            string publicationKey = PublicationKey(manifest.worldId, source.chunkId);
            _chunkPublicationTails.TryGetValue(publicationKey, out Task previous);
            _backgroundPublicationCount++;
            SetFinalizationStatus($"Scanning; persisting {_backgroundPublicationCount} chunk(s)");
            Task tail = PersistFinalizedChunkAfterAsync(previous, store, manifest, source,
                snapshot, keyframeDirectory);
            _chunkPublicationTails[publicationKey] = tail;
        }

        private async Task PersistFinalizedChunkAfterAsync(Task previous, WorldStore store,
            WorldManifest manifest, ChunkRecord source,
            ChunkGpuSnapshot snapshot, string keyframeDirectory)
        {
            try
            {
                if (previous != null)
                    await previous;
                const int maximumAttempts = 3;
                string lastError = null;
                for (int attempt = 1; attempt <= maximumAttempts; attempt++)
                {
                    long now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        manifest.updatedUnixMilliseconds);
                    ChunkSnapshotPublishResult publication =
                        await ChunkSnapshotPublisher.PublishAsync(store,
                            manifest, source, snapshot, now,
                            keyframeDirectory, ChunkLifecycleState.Persisted);
                    if (publication.Success)
                    {
                        Logger.Info($"Background chunk publication complete: " +
                                    $"{source.chunkId} revision {publication.Revision}");
                        ChunkRevisionPublished?.Invoke(store, manifest.worldId,
                            source.chunkId, publication.Revision);
                        return;
                    }

                    lastError = publication.Error;
                    if (attempt < maximumAttempts)
                    {
                        Logger.Warning($"Background publication retry {attempt}/" +
                                       $"{maximumAttempts} for {source.chunkId}: {lastError}");
                        await Task.Delay(attempt * 1_000);
                    }
                }
                Logger.Error($"Background publication failed for {source.chunkId}: " +
                             lastError);
                SetFinalizationStatus("Background persistence failed: " + lastError);
            }
            catch (Exception exception)
            {
                Logger.Error($"Background publication crashed for {source?.chunkId}: " +
                             exception);
                SetFinalizationStatus("Background persistence failed: " + exception.Message);
            }
            finally
            {
                _backgroundPublicationCount = Math.Max(0, _backgroundPublicationCount - 1);
                if (_backgroundPublicationCount == 0 &&
                    !FinalizationStatus.StartsWith("Background persistence failed",
                        StringComparison.Ordinal))
                    SetFinalizationStatus("Idle");
                else if (_backgroundPublicationCount > 0)
                    SetFinalizationStatus($"Scanning; persisting " +
                                          $"{_backgroundPublicationCount} chunk(s)");
            }
        }

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

        private void OnRenderModeChanged(ScanRenderMode mode)
        {
            _meshCache?.SetRenderMode(mode);
            _diffSoupCache?.SetRenderMode(mode);
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

        private static VolumeArtifactLoadResult LoadVolumeArtifact(WorldStore store,
            string worldId, ChunkArtifactRecord artifact)
        {
            if (!store.TryResolveVerifiedArtifact(worldId, artifact, out string path,
                    out string error))
                return new VolumeArtifactLoadResult { Error = error };
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                return ChunkSnapshotCodec.TryReadVolume(stream,
                    out ChunkVolumeSnapshot snapshot, out error)
                    ? new VolumeArtifactLoadResult { Snapshot = snapshot }
                    : new VolumeArtifactLoadResult { Error = error };
            }
            catch (Exception exception)
            {
                return new VolumeArtifactLoadResult
                {
                    Error = "Volume artifact read failed: " + exception.Message
                };
            }
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

        private static bool PoseApproximately(RigidPoseData left, RigidPoseData right)
        {
            return Vector3.Distance(left.position, right.position) <= 0.001f &&
                   Quaternion.Angle(left.rotation, right.rotation) <= 0.05f;
        }

        private static bool IsCompatibleVolumeSnapshot(ChunkVolumeSnapshot snapshot,
            ChunkRecord chunk, VolumeIntegrator volume, out string error)
        {
            error = null;
            if (snapshot == null || chunk == null || volume == null)
            {
                error = "A revisit requires an available target volume snapshot.";
                return false;
            }
            int3 expected = volume.VoxelCount;
            if (snapshot.VoxelCount.x != expected.x ||
                snapshot.VoxelCount.y != expected.y ||
                snapshot.VoxelCount.z != expected.z ||
                Mathf.Abs(snapshot.VoxelSize - volume.VoxelSize) > 0.0001f)
            {
                error = "The revisit target volume is incompatible with the mapper layout.";
                return false;
            }
            return true;
        }

        private sealed class OverlapObservation
        {
            internal OverlapObservation(string worldId, string chunkId,
                int chunkRevision, OverlapPointCloud cloud)
            {
                WorldId = worldId;
                ChunkId = chunkId;
                ChunkRevision = chunkRevision;
                Cloud = cloud;
            }

            internal string WorldId { get; }
            internal string ChunkId { get; }
            internal int ChunkRevision { get; }
            internal OverlapPointCloud Cloud { get; }
        }

        private sealed class VolumeArtifactLoadResult
        {
            public ChunkVolumeSnapshot Snapshot;
            public string Error;
        }

        private sealed class KeyframeRestoreResult
        {
            public bool Success;
            public string Error;
            public string QuarantinedDirectory;
        }
    }
}
