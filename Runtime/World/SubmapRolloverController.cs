using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    [Serializable]
    public sealed class SubmapRolloverSettings
    {
        public const float DefaultBoundaryMarginMeters = 1.0f;
        public const float DefaultOverlapMeters = 2.0f;
        public const float DefaultRearmHysteresisMeters = 0.75f;
        public const float DefaultEmergencyBoundaryMarginMeters = 0.2f;
        public const int DefaultCooldownMilliseconds = 1_000;
        public const int DefaultMaximumResidentChunkMeshes = 3;

        [Min(0.1f)] public float boundaryMarginMeters = DefaultBoundaryMarginMeters;
        [Min(0.1f)] public float overlapMeters = DefaultOverlapMeters;
        [Min(0.05f)] public float rearmHysteresisMeters = DefaultRearmHysteresisMeters;
        [Min(0.05f)] public float emergencyBoundaryMarginMeters =
            DefaultEmergencyBoundaryMarginMeters;
        [Min(0)] public int cooldownMilliseconds = DefaultCooldownMilliseconds;
        [Range(1, 8)] public int maximumResidentChunkMeshes =
            DefaultMaximumResidentChunkMeshes;

        public void ApplyLargeWorldDefaults()
        {
            boundaryMarginMeters = DefaultBoundaryMarginMeters;
            overlapMeters = DefaultOverlapMeters;
            rearmHysteresisMeters = DefaultRearmHysteresisMeters;
            emergencyBoundaryMarginMeters = DefaultEmergencyBoundaryMarginMeters;
            cooldownMilliseconds = DefaultCooldownMilliseconds;
            maximumResidentChunkMeshes = DefaultMaximumResidentChunkMeshes;
        }

        public bool UsesLargeWorldDefaults =>
            Mathf.Approximately(boundaryMarginMeters, DefaultBoundaryMarginMeters) &&
            Mathf.Approximately(overlapMeters, DefaultOverlapMeters) &&
            Mathf.Approximately(rearmHysteresisMeters,
                DefaultRearmHysteresisMeters) &&
            Mathf.Approximately(emergencyBoundaryMarginMeters,
                DefaultEmergencyBoundaryMarginMeters) &&
            cooldownMilliseconds == DefaultCooldownMilliseconds &&
            maximumResidentChunkMeshes == DefaultMaximumResidentChunkMeshes;
    }

    public sealed class SubmapRolloverRequest
    {
        public string SourceChunkId { get; internal set; }
        public string TargetChunkId { get; internal set; }
        public int BoundaryAxis { get; internal set; }
        public int BoundaryDirection { get; internal set; }
        public Vector3 CameraInSourceChunk { get; internal set; }
        public RigidPoseData SourceFromTarget { get; internal set; }
        public RigidPoseData WorldFromTarget { get; internal set; }
        public bool IsRevisit { get; internal set; }
        public long RequestedUnixMilliseconds { get; internal set; }
    }

    /// <summary>
    /// Deterministic rollover state machine. It owns no GPU resources: one caller finalizes
    /// the source payload, then commits the request and reuses the sole TSDF for the target.
    /// </summary>
    public sealed class SubmapRolloverController
    {
        private readonly WorldManifest _manifest;
        private readonly SubmapRolloverSettings _settings;
        private ChunkRecord _activeChunk;
        private SubmapRolloverRequest _pending;
        private bool _armed = true;
        private long _lastCommitUnixMilliseconds = long.MinValue;
        private string _previousChunkId;
        private int _entryBoundaryAxis = -1;
        private int _entryBoundaryDirection;

        private SubmapRolloverController(WorldManifest manifest, ChunkRecord activeChunk,
            SubmapRolloverSettings settings)
        {
            _manifest = manifest;
            _activeChunk = activeChunk;
            _settings = settings;
        }

        public WorldManifest Manifest => _manifest;
        public ChunkRecord ActiveChunk => _activeChunk;
        public SubmapRolloverRequest PendingRequest => _pending;
        public bool IsArmed => _armed;
        public int ResidentVolumeCount => _activeChunk == null ? 0 : 1;
        public int MaximumResidentVolumeCount => 1;

        public static bool TryCreate(WorldManifest manifest, SubmapRolloverSettings settings,
            out SubmapRolloverController controller, out string error)
        {
            controller = null;
            error = null;
            WorldValidationResult validation = WorldManifestValidator.Validate(manifest);
            if (!validation.IsValid)
            {
                error = validation.ToString();
                return false;
            }
            if (!ValidateSettings(settings, out error))
                return false;

            ChunkRecord active = null;
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                if (manifest.chunks[i].state != ChunkLifecycleState.Active)
                    continue;
                if (active != null)
                {
                    error = "World manifest contains more than one active chunk.";
                    return false;
                }
                active = manifest.chunks[i];
            }
            if (active == null)
            {
                error = "World manifest has no active chunk.";
                return false;
            }
            if (!TryGetThresholds(active.localBounds, settings, out _, out _, out error))
                return false;

            controller = new SubmapRolloverController(manifest, active, settings);
            return true;
        }

        public bool TryObserveCamera(Vector3 cameraWorldPosition, long unixMilliseconds,
            out SubmapRolloverRequest request, out string error)
        {
            request = _pending;
            error = null;
            if (_pending != null)
                return true;
            if (unixMilliseconds < 0)
            {
                error = "Observation timestamp cannot be negative.";
                return false;
            }
            if (!IsFinite(cameraWorldPosition))
            {
                error = "Camera position must be finite.";
                return false;
            }

            RigidPoseData chunkFromWorld = _activeChunk.worldFromChunk.Inverse();
            Vector3 cameraInChunk = chunkFromWorld.TransformPoint(cameraWorldPosition);
            Vector3 offset = cameraInChunk - _activeChunk.localBounds.center;
            if (!TryGetThresholds(_activeChunk.localBounds, _settings,
                    out Vector3 trigger, out Vector3 hardLimit, out error))
                return false;

            Vector3 rearm = new(
                Mathf.Max(0.01f, trigger.x - _settings.rearmHysteresisMeters),
                Mathf.Max(0.01f, trigger.y - _settings.rearmHysteresisMeters),
                Mathf.Max(0.01f, trigger.z - _settings.rearmHysteresisMeters));
            if (!_armed && IsInside(offset, rearm))
                _armed = true;

            bool emergency = !_armed && IsOutside(offset, hardLimit);
            if (!_armed && !emergency)
                return false;
            if (_lastCommitUnixMilliseconds != long.MinValue && !emergency &&
                unixMilliseconds - _lastCommitUnixMilliseconds <
                _settings.cooldownMilliseconds)
                return false;

            int axis = DominantBoundaryAxis(offset, trigger, out float normalizedDistance);
            if (normalizedDistance < 1f && !emergency)
                return false;
            if (emergency)
                axis = DominantBoundaryAxis(offset, hardLimit, out _);
            int direction = Component(offset, axis) >= 0f ? 1 : -1;
            float extent = Component(_activeChunk.localBounds.extents, axis);
            float shift = extent * 2f - _settings.overlapMeters;
            if (shift <= 0f)
            {
                error = "Chunk overlap must be smaller than the selected volume dimension.";
                return false;
            }

            Vector3 translation = Vector3.zero;
            SetComponent(ref translation, axis, direction * shift);
            var sourceFromTarget = new RigidPoseData(translation, Quaternion.identity);
            RigidPoseData proposedWorldFromTarget = _activeChunk.worldFromChunk *
                                                    sourceFromTarget;
            ChunkRecord existingTarget = FindReusableChunk(_manifest, _activeChunk,
                proposedWorldFromTarget, _activeChunk.localBounds);
            bool exactReverse = existingTarget != null &&
                string.Equals(existingTarget.chunkId, _previousChunkId,
                    StringComparison.Ordinal) &&
                axis == _entryBoundaryAxis && direction == -_entryBoundaryDirection;
            if (exactReverse)
            {
                // Source and target use the same nominal trigger plane when overlap is
                // twice the boundary margin (the default 2 m / 1 m setup). Keep a real
                // Schmitt band even after the controller has re-armed: an exact A<->B
                // reversal must cross deeper than the plane that caused the prior switch.
                float reverseThreshold = Component(trigger, axis) +
                                         _settings.rearmHysteresisMeters;
                if (Mathf.Abs(Component(offset, axis)) < reverseThreshold)
                    return false;
            }
            if (existingTarget != null)
            {
                proposedWorldFromTarget = existingTarget.worldFromChunk;
                sourceFromTarget = _activeChunk.worldFromChunk.Inverse() *
                                   existingTarget.worldFromChunk;
            }
            _pending = new SubmapRolloverRequest
            {
                SourceChunkId = _activeChunk.chunkId,
                TargetChunkId = existingTarget?.chunkId ?? NextChunkId(_manifest),
                BoundaryAxis = axis,
                BoundaryDirection = direction,
                CameraInSourceChunk = cameraInChunk,
                SourceFromTarget = sourceFromTarget,
                WorldFromTarget = proposedWorldFromTarget,
                IsRevisit = existingTarget != null,
                RequestedUnixMilliseconds = unixMilliseconds
            };
            request = _pending;
            return true;
        }

        /// <summary>
        /// Publishes a pending transition. The default overload is used when the caller has
        /// already durably finalized the source chunk. Real-time rollover uses the overload
        /// with <see cref="ChunkLifecycleState.Finalizing"/> so the reusable GPU volume can
        /// advance before the large source payload finishes writing in the background.
        /// </summary>
        public bool TryCommitPending(WorldStore store, long unixMilliseconds,
            out ChunkRecord newActiveChunk, out string error)
        {
            return TryCommitPending(store, unixMilliseconds,
                ChunkLifecycleState.Persisted, out newActiveChunk, out error);
        }

        public bool TryCommitPending(WorldStore store, long unixMilliseconds,
            ChunkLifecycleState sourceState, out ChunkRecord newActiveChunk,
            out string error)
        {
            newActiveChunk = null;
            error = null;
            if (_pending == null)
            {
                error = "There is no pending rollover request.";
                return false;
            }
            if (unixMilliseconds < _pending.RequestedUnixMilliseconds ||
                unixMilliseconds < _manifest.updatedUnixMilliseconds)
            {
                error = "Rollover commit timestamp is stale.";
                return false;
            }
            if (_manifest.revision == int.MaxValue)
            {
                error = "World revision is exhausted.";
                return false;
            }
            if (sourceState != ChunkLifecycleState.Finalizing &&
                sourceState != ChunkLifecycleState.Persisted &&
                sourceState != ChunkLifecycleState.Cached)
            {
                error = "A rollover source must become finalizing, persisted, or cached.";
                return false;
            }

            ChunkLifecycleState oldState = _activeChunk.state;
            long oldChunkUpdated = _activeChunk.updatedUnixMilliseconds;
            long oldWorldUpdated = _manifest.updatedUnixMilliseconds;
            int oldWorldRevision = _manifest.revision;
            ChunkRecord target = null;
            bool addedTarget = !_pending.IsRevisit;
            if (_pending.IsRevisit)
            {
                target = _manifest.chunks.Find(candidate => candidate != null &&
                    string.Equals(candidate.chunkId, _pending.TargetChunkId,
                        StringComparison.Ordinal));
                if (target == null || ReferenceEquals(target, _activeChunk) ||
                    target.state == ChunkLifecycleState.Active ||
                    target.state == ChunkLifecycleState.New ||
                    target.state == ChunkLifecycleState.Failed)
                {
                    error = "The requested revisit target is not reusable.";
                    return false;
                }
            }
            else
            {
                target = new ChunkRecord
                {
                    chunkId = _pending.TargetChunkId,
                    revision = 0,
                    state = ChunkLifecycleState.Active,
                    worldFromChunk = _pending.WorldFromTarget,
                    localBounds = _activeChunk.localBounds,
                    anchorId = _manifest.worldAnchorId,
                    createdUnixMilliseconds = unixMilliseconds,
                    updatedUnixMilliseconds = unixMilliseconds,
                    quality = 0f,
                    artifacts = new List<ChunkArtifactRecord>()
                };
            }
            ChunkLifecycleState oldTargetState = target.state;
            long oldTargetUpdated = target.updatedUnixMilliseconds;
            PoseGraphConstraintKind edgeKind = _pending.IsRevisit
                ? PoseGraphConstraintKind.Overlap
                : PoseGraphConstraintKind.Tracking;
            string edgeProvenance = _pending.IsRevisit
                ? "submap-revisit"
                : "submap-rollover";
            if (!PoseGraphConstraintFactory.TryCreate(NextEdgeId(_manifest),
                    _activeChunk.chunkId, target.chunkId, edgeKind,
                    _pending.SourceFromTarget, 1f,
                    new[] { 0.01f, 0.01f, 0.01f, 0.02f, 0.02f, 0.02f },
                    _pending.RequestedUnixMilliseconds, edgeProvenance,
                    out PoseGraphEdgeRecord edge, out error))
                return false;

            _activeChunk.state = sourceState;
            _activeChunk.updatedUnixMilliseconds = unixMilliseconds;
            if (addedTarget)
                _manifest.chunks.Add(target);
            else
            {
                target.state = ChunkLifecycleState.Active;
                target.updatedUnixMilliseconds = unixMilliseconds;
            }
            _manifest.edges.Add(edge);
            _manifest.updatedUnixMilliseconds = unixMilliseconds;
            _manifest.revision++;

            WorldValidationResult validation = WorldManifestValidator.Validate(_manifest);
            bool valid = validation.IsValid;
            bool persisted = valid && (store == null ||
                store.TryCommitManifest(_manifest, out error));
            if (!persisted)
            {
                _manifest.edges.RemoveAt(_manifest.edges.Count - 1);
                if (addedTarget)
                    _manifest.chunks.RemoveAt(_manifest.chunks.Count - 1);
                else
                {
                    target.state = oldTargetState;
                    target.updatedUnixMilliseconds = oldTargetUpdated;
                }
                _activeChunk.state = oldState;
                _activeChunk.updatedUnixMilliseconds = oldChunkUpdated;
                _manifest.updatedUnixMilliseconds = oldWorldUpdated;
                _manifest.revision = oldWorldRevision;
                if (!valid)
                    error = validation.ToString();
                return false;
            }

            string previousChunkId = _activeChunk.chunkId;
            _activeChunk = target;
            _previousChunkId = previousChunkId;
            _entryBoundaryAxis = _pending.BoundaryAxis;
            _entryBoundaryDirection = _pending.BoundaryDirection;
            _lastCommitUnixMilliseconds = unixMilliseconds;
            _pending = null;
            _armed = false;
            newActiveChunk = target;
            return true;
        }

        public void CancelPending()
        {
            _pending = null;
        }

        private static bool ValidateSettings(SubmapRolloverSettings settings, out string error)
        {
            error = null;
            if (settings == null || !IsFinite(settings.boundaryMarginMeters) ||
                !IsFinite(settings.overlapMeters) ||
                !IsFinite(settings.rearmHysteresisMeters) ||
                !IsFinite(settings.emergencyBoundaryMarginMeters) ||
                settings.boundaryMarginMeters <= 0f || settings.overlapMeters <= 0f ||
                settings.rearmHysteresisMeters <= 0f ||
                settings.emergencyBoundaryMarginMeters <= 0f ||
                settings.rearmHysteresisMeters >=
                    settings.boundaryMarginMeters - settings.emergencyBoundaryMarginMeters ||
                settings.cooldownMilliseconds < 0 ||
                settings.maximumResidentChunkMeshes < 1)
            {
                error = "Submap rollover settings are outside supported limits.";
                return false;
            }
            return true;
        }

        private static bool TryGetThresholds(BoundsData bounds,
            SubmapRolloverSettings settings, out Vector3 trigger, out Vector3 hardLimit,
            out string error)
        {
            Vector3 extents = bounds.extents;
            trigger = extents - Vector3.one * settings.boundaryMarginMeters;
            hardLimit = extents - Vector3.one * settings.emergencyBoundaryMarginMeters;
            error = null;
            if (trigger.x <= 0f || trigger.y <= 0f || trigger.z <= 0f ||
                hardLimit.x <= trigger.x || hardLimit.y <= trigger.y ||
                hardLimit.z <= trigger.z || settings.overlapMeters >= extents.x * 2f ||
                settings.overlapMeters >= extents.y * 2f ||
                settings.overlapMeters >= extents.z * 2f)
            {
                error = "Rollover margins/overlap do not fit inside chunk bounds.";
                return false;
            }
            return true;
        }

        private static int DominantBoundaryAxis(Vector3 offset, Vector3 threshold,
            out float normalizedDistance)
        {
            float x = Mathf.Abs(offset.x) / threshold.x;
            float y = Mathf.Abs(offset.y) / threshold.y;
            float z = Mathf.Abs(offset.z) / threshold.z;
            if (x >= y && x >= z)
            {
                normalizedDistance = x;
                return 0;
            }
            if (y >= z)
            {
                normalizedDistance = y;
                return 1;
            }
            normalizedDistance = z;
            return 2;
        }

        private static bool IsInside(Vector3 value, Vector3 limits)
        {
            return Mathf.Abs(value.x) <= limits.x && Mathf.Abs(value.y) <= limits.y &&
                   Mathf.Abs(value.z) <= limits.z;
        }

        private static bool IsOutside(Vector3 value, Vector3 limits)
        {
            return Mathf.Abs(value.x) >= limits.x || Mathf.Abs(value.y) >= limits.y ||
                   Mathf.Abs(value.z) >= limits.z;
        }

        private static float Component(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        private static void SetComponent(ref Vector3 value, int axis, float component)
        {
            if (axis == 0) value.x = component;
            else if (axis == 1) value.y = component;
            else value.z = component;
        }

        private static string NextChunkId(WorldManifest manifest)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.chunks.Count; i++)
                used.Add(manifest.chunks[i].chunkId);
            int sequence = manifest.chunks.Count;
            string candidate;
            do { candidate = $"chunk-{sequence++:D6}"; }
            while (used.Contains(candidate));
            return candidate;
        }

        private static ChunkRecord FindReusableChunk(WorldManifest manifest,
            ChunkRecord activeChunk, RigidPoseData proposedWorldFromTarget,
            BoundsData targetBounds)
        {
            const float maximumOriginDistance = 0.25f;
            const float maximumRotationDegrees = 5f;
            ChunkRecord closest = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord candidate = manifest.chunks[i];
                if (candidate == null || ReferenceEquals(candidate, activeChunk) ||
                    candidate.state == ChunkLifecycleState.Active ||
                    candidate.state == ChunkLifecycleState.New ||
                    candidate.state == ChunkLifecycleState.Failed ||
                    Vector3.Distance(candidate.localBounds.center, targetBounds.center) > 0.01f ||
                    Vector3.Distance(candidate.localBounds.extents, targetBounds.extents) > 0.01f ||
                    Quaternion.Angle(candidate.worldFromChunk.rotation,
                        proposedWorldFromTarget.rotation) > maximumRotationDegrees)
                    continue;

                float distance = Vector3.Distance(candidate.worldFromChunk.position,
                    proposedWorldFromTarget.position);
                if (distance <= maximumOriginDistance && distance < closestDistance)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        private static string NextEdgeId(WorldManifest manifest)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.edges.Count; i++)
                used.Add(manifest.edges[i].edgeId);
            int sequence = manifest.edges.Count;
            string candidate;
            do { candidate = $"edge-{sequence++:D8}"; }
            while (used.Contains(candidate));
            return candidate;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class WorldSessionFactory
    {
        public static bool TryCreate(WorldStore store, string worldId, string displayName,
            RigidPoseData worldFromInitialChunk, BoundsData localBounds,
            long unixMilliseconds, out WorldManifest manifest, out string error)
        {
            if (store != null && StoragePath.IsSafeIdentifier(worldId, 96) &&
                System.IO.Directory.Exists(store.GetWorldDirectory(worldId)))
            {
                manifest = null;
                error = $"World '{worldId}' already exists.";
                return false;
            }
            manifest = new WorldManifest
            {
                worldId = worldId,
                displayName = displayName ?? string.Empty,
                createdUnixMilliseconds = unixMilliseconds,
                updatedUnixMilliseconds = unixMilliseconds,
                revision = 0,
                worldAnchorId = string.Empty,
                chunks = new List<ChunkRecord>
                {
                    new()
                    {
                        chunkId = "chunk-000000",
                        revision = 0,
                        state = ChunkLifecycleState.Active,
                        worldFromChunk = worldFromInitialChunk,
                        localBounds = localBounds,
                        anchorId = string.Empty,
                        createdUnixMilliseconds = unixMilliseconds,
                        updatedUnixMilliseconds = unixMilliseconds,
                        quality = 0f,
                        artifacts = new List<ChunkArtifactRecord>()
                    }
                },
                edges = new List<PoseGraphEdgeRecord>()
            };
            WorldValidationResult validation = WorldManifestValidator.Validate(manifest);
            if (!validation.IsValid)
            {
                error = validation.ToString();
                manifest = null;
                return false;
            }
            if (store != null && !store.TryCommitManifest(manifest, out error))
            {
                manifest = null;
                return false;
            }
            error = null;
            return true;
        }
    }
}
